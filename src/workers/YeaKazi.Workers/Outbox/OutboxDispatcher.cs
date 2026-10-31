using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace YeaKazi.Workers.Outbox;

public sealed record OutboxEnvelope(Guid Id, Guid TenantId, string Type, JsonDocument Payload, int Attempts, string? CorrelationId);

/// <summary>Handles one outbox message type. Handlers must be idempotent (delivery is at-least-once).</summary>
public interface IOutboxHandler
{
    string Type { get; }

    Task HandleAsync(OutboxEnvelope message, CancellationToken ct);
}

public sealed class OutboxOptions
{
    public const string Section = "Outbox";
    public int BatchSize { get; set; } = 20;
    public int PollSeconds { get; set; } = 5;
    public int MaxAttempts { get; set; } = 8;
}

/// <summary>
/// Claims pending outbox rows with FOR UPDATE SKIP LOCKED (safe with several worker
/// instances), runs the handler and records the outcome. Failures back off
/// exponentially and dead-letter after MaxAttempts.
/// </summary>
public sealed class OutboxDispatcher(
    WorkerDataSource dataSource,
    IEnumerable<IOutboxHandler> handlers,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcher> logger)
{
    private readonly Dictionary<string, IOutboxHandler> _handlers = handlers.ToDictionary(h => h.Type);
    private readonly OutboxOptions _options = options.Value;

    public async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var batch = new List<OutboxEnvelope>();
        await using (var select = new NpgsqlCommand("""
            select id, tenant_id, type, payload, attempts, correlation_id
            from messaging.outbox_messages
            where processed_at is null and dead_lettered_at is null and next_attempt_at <= now()
            order by occurred_at
            limit @limit
            for update skip locked
            """, conn, tx))
        {
            select.Parameters.AddWithValue("limit", _options.BatchSize);
            await using var reader = await select.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                batch.Add(new OutboxEnvelope(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    JsonDocument.Parse(reader.GetString(3)),
                    reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5)));
            }
        }

        foreach (var message in batch)
        {
            using var scope = logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = message.CorrelationId, ["OutboxId"] = message.Id });
            try
            {
                if (_handlers.TryGetValue(message.Type, out var handler))
                {
                    await handler.HandleAsync(message, ct);
                }
                else
                {
                    logger.LogDebug("No subscriber for outbox type {Type}", message.Type);
                }

                await MarkProcessedAsync(conn, tx, message.Id, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var attempts = message.Attempts + 1;
                var dead = attempts >= _options.MaxAttempts;
                logger.LogWarning("Outbox message {Type} failed (attempt {Attempts}){Dead}", message.Type, attempts, dead ? ", dead-lettered" : string.Empty);
                await MarkFailedAsync(conn, tx, message.Id, attempts, dead, ex.GetType().Name + ": " + Truncate(ex.Message), ct);
            }
        }

        await tx.CommitAsync(ct);
        return batch.Count;
    }

    internal static TimeSpan Backoff(int attempts) => TimeSpan.FromSeconds(Math.Min(3600, 15 * Math.Pow(2, attempts - 1)));

    private static async Task MarkProcessedAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid id, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("update messaging.outbox_messages set processed_at = now(), last_error = null where id = @id", conn, tx);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task MarkFailedAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid id, int attempts, bool dead, string error, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            update messaging.outbox_messages
            set attempts = @attempts,
                last_error = @error,
                next_attempt_at = now() + @backoff,
                dead_lettered_at = case when @dead then now() else null end
            where id = @id
            """, conn, tx);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("attempts", attempts);
        cmd.Parameters.AddWithValue("error", error);
        cmd.Parameters.AddWithValue("backoff", Backoff(attempts));
        cmd.Parameters.AddWithValue("dead", dead);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string Truncate(string s) => s.Length <= 1500 ? s : s[..1500];
}

/// <summary>Data source on the yeakazi_worker role (outbox only; no tenant HR data).</summary>
public sealed class WorkerDataSource(NpgsqlDataSource value)
{
    public NpgsqlDataSource Value { get; } = value;
}
