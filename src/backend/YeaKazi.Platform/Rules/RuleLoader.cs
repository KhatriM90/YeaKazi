using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlTypes;

namespace YeaKazi.Platform.Rules;

public sealed record RuleLoadResult(int Loaded, int Unchanged, IReadOnlyList<string> Messages);

/// <summary>
/// Loads /rules JSON files into rules.rule_versions as the schema owner. Versions are
/// immutable: an unchanged file is skipped, a changed file under an existing version label
/// is rejected, and overlapping effective periods of the same rule are rejected. Each load
/// is recorded in the platform audit log.
/// </summary>
public static class RuleLoader
{
    private static readonly string[] Statuses = ["seed_pending_specialist_signoff", "approved"];

    public static async Task<RuleLoadResult> LoadAsync(string ownerConnectionString, string rootPath, CancellationToken ct = default)
    {
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Rules folder '{rootPath}' does not exist.");
        }

        var files = Directory.EnumerateFiles(rootPath, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();
        var parsed = files.Select(f => Parse(f, Path.GetRelativePath(rootPath, f).Replace('\\', '/'))).ToList();

        await using var conn = new NpgsqlConnection(ownerConnectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await Exec(conn, tx, "select set_config('app.scope', 'platform', true)", ct);
        await Exec(conn, tx, "lock table rules.rule_versions in exclusive mode", ct);

        int loaded = 0, unchanged = 0;
        var messages = new List<string>();
        foreach (var r in parsed)
        {
            await using (var find = new NpgsqlCommand(
                "select content_hash from rules.rule_versions where jurisdiction = @j and rule_code = @c and version_label = @v", conn, tx))
            {
                find.Parameters.AddWithValue("j", r.Jurisdiction);
                find.Parameters.AddWithValue("c", r.RuleCode);
                find.Parameters.AddWithValue("v", r.Version);
                var existing = (string?)await find.ExecuteScalarAsync(ct);
                if (existing is not null)
                {
                    if (existing != r.Hash)
                    {
                        throw new InvalidOperationException(
                            $"{r.File}: {r.RuleCode} version {r.Version} is already loaded with different content. Publish a new version instead of editing a loaded one.");
                    }

                    unchanged++;
                    continue;
                }
            }

            await using (var overlap = new NpgsqlCommand("""
                select version_label from rules.rule_versions
                where jurisdiction = @j and rule_code = @c
                  and effective_from <= coalesce(@to, 'infinity'::date)
                  and coalesce(effective_to, 'infinity'::date) >= @from
                limit 1
                """, conn, tx))
            {
                overlap.Parameters.AddWithValue("j", r.Jurisdiction);
                overlap.Parameters.AddWithValue("c", r.RuleCode);
                overlap.Parameters.AddWithValue("from", r.EffectiveFrom);
                overlap.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.Date) { Value = (object?)r.EffectiveTo ?? DBNull.Value });
                var clash = (string?)await overlap.ExecuteScalarAsync(ct);
                if (clash is not null)
                {
                    throw new InvalidOperationException(
                        $"{r.File}: {r.RuleCode} version {r.Version} overlaps active version {clash}. End the earlier version first.");
                }
            }

            var id = Guid.CreateVersion7();
            await using (var insert = new NpgsqlCommand("""
                insert into rules.rule_versions (id, jurisdiction, rule_code, version_label, effective_from, effective_to,
                  authority_source, applicability, reviewer, approved_at, status, payload, content_hash, source_file, loaded_at)
                values (@id, @j, @c, @v, @from, @to, @src, @app, @rev, @appr, @status, @payload::jsonb, @hash, @file, now())
                """, conn, tx))
            {
                insert.Parameters.AddWithValue("id", id);
                insert.Parameters.AddWithValue("j", r.Jurisdiction);
                insert.Parameters.AddWithValue("c", r.RuleCode);
                insert.Parameters.AddWithValue("v", r.Version);
                insert.Parameters.AddWithValue("from", r.EffectiveFrom);
                insert.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.Date) { Value = (object?)r.EffectiveTo ?? DBNull.Value });
                insert.Parameters.AddWithValue("src", r.AuthoritySource);
                insert.Parameters.Add(new NpgsqlParameter("app", NpgsqlDbType.Varchar) { Value = (object?)r.Applicability ?? DBNull.Value });
                insert.Parameters.Add(new NpgsqlParameter("rev", NpgsqlDbType.Varchar) { Value = (object?)r.Reviewer ?? DBNull.Value });
                insert.Parameters.Add(new NpgsqlParameter("appr", NpgsqlDbType.Date) { Value = (object?)r.ApprovedAt ?? DBNull.Value });
                insert.Parameters.AddWithValue("status", r.Status);
                insert.Parameters.AddWithValue("payload", r.Payload);
                insert.Parameters.AddWithValue("hash", r.Hash);
                insert.Parameters.AddWithValue("file", r.File);
                await insert.ExecuteNonQueryAsync(ct);
            }

            await using (var audit = new NpgsqlCommand("""
                insert into platform.platform_audit_events (id, actor_kind, actor_display, occurred_at, action, entity_type, entity_id, details)
                values (@id, 'system', 'rule loader', now(), 'rules.version_loaded', 'rule_version', @eid, @details::jsonb)
                """, conn, tx))
            {
                audit.Parameters.AddWithValue("id", Guid.CreateVersion7());
                audit.Parameters.AddWithValue("eid", id.ToString());
                audit.Parameters.AddWithValue("details", JsonSerializer.Serialize(new
                {
                    jurisdiction = r.Jurisdiction, ruleCode = r.RuleCode, version = r.Version,
                    effectiveFrom = r.EffectiveFrom, effectiveTo = r.EffectiveTo, contentHash = r.Hash, file = r.File,
                }));
                await audit.ExecuteNonQueryAsync(ct);
            }

            loaded++;
            messages.Add($"Loaded {r.Jurisdiction} {r.RuleCode} {r.Version} ({r.EffectiveFrom:yyyy-MM-dd} to {(r.EffectiveTo?.ToString("yyyy-MM-dd") ?? "open")}).");
        }

        await tx.CommitAsync(ct);
        return new RuleLoadResult(loaded, unchanged, messages);
    }

    internal sealed record ParsedRule(string File, string Jurisdiction, string RuleCode, string Version, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
        string AuthoritySource, string? Applicability, string? Reviewer, DateOnly? ApprovedAt, string Status, string Payload, string Hash);

    internal static ParsedRule Parse(string path, string relative)
    {
        var text = File.ReadAllText(path);
        var node = JsonNode.Parse(text)?.AsObject() ?? throw new InvalidOperationException($"{relative}: not a JSON object.");
        string Req(string name) => node[name]?.GetValue<string>() is { Length: > 0 } v ? v : throw new InvalidOperationException($"{relative}: '{name}' is required.");
        string? Opt(string name) => node[name]?.GetValue<string>();
        DateOnly Date(string value, string name) => DateOnly.TryParseExact(value, "yyyy-MM-dd", out var d) ? d : throw new InvalidOperationException($"{relative}: '{name}' must be yyyy-MM-dd.");

        var status = Req("status");
        if (!Statuses.Contains(status))
        {
            throw new InvalidOperationException($"{relative}: status must be one of {string.Join(", ", Statuses)}.");
        }

        var from = Date(Req("effectiveFrom"), "effectiveFrom");
        DateOnly? to = Opt("effectiveTo") is { } t ? Date(t, "effectiveTo") : null;
        if (to < from)
        {
            throw new InvalidOperationException($"{relative}: effectiveTo is before effectiveFrom.");
        }

        if (status == "approved" && (Opt("reviewer") is null || Opt("approvedAt") is null))
        {
            throw new InvalidOperationException($"{relative}: an approved rule needs reviewer and approvedAt.");
        }

        var payload = node["payload"] as JsonObject ?? throw new InvalidOperationException($"{relative}: 'payload' object is required.");
        var canonical = node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return new ParsedRule(relative, Req("jurisdiction"), Req("ruleCode"), Req("version"), from, to, Req("authoritySource"),
            Opt("applicability"), Opt("reviewer"), Opt("approvedAt") is { } a ? Date(a, "approvedAt") : null, status,
            payload.ToJsonString(), hash);
    }

    private static async Task Exec(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
