using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using YeaKazi.BuildingBlocks.Audit;
using YeaKazi.BuildingBlocks.Tenancy;
using YeaKazi.Platform.Domain;
using YeaKazi.Platform.Persistence;

namespace YeaKazi.Platform.Auth;

public sealed record RegenerateRecoveryCodesRequest(string Code);

public sealed record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);

public sealed record MfaRecoverRequest(string ChallengeToken, string RecoveryCode);

public sealed record ResetMfaRequest(uint Version, string Reason);

public sealed record SecurityNoticeMessage(Guid UserId, string Email, string DisplayName, string TenantName, string Kind, DateTimeOffset OccurredAt);

public static class SecurityNoticeKinds
{
    public const string RecoveryCodeUsed = "mfa_recovery_code_used";
    public const string RecoveryCodesRegenerated = "mfa_recovery_codes_regenerated";
    public const string MfaReset = "mfa_reset_by_administrator";
    public const string OutboxType = "iam.security_notice";
}

/// <summary>
/// Single-use MFA recovery codes (docs/assumptions.md A-19). Ten codes of 50 random bits
/// each, shown once; only SHA-256(user id | code) is stored. Generating a new set revokes the
/// old one. Using a code is audited and the user is told by email.
/// </summary>
public sealed class MfaRecoveryService(PlatformDbContext db, AuditTrail audit, RequestContext context, TimeProvider clock)
{
    public const int CodeCount = 10;
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    internal async Task<IReadOnlyList<string>> IssueAsync(User user, CancellationToken ct)
    {
        await RevokeAllAsync(user.Id, ct);
        var codes = new List<string>(CodeCount);
        for (var i = 0; i < CodeCount; i++)
        {
            var code = NewCode();
            codes.Add(code);
            db.MfaRecoveryCodes.Add(new MfaRecoveryCode { UserId = user.Id, CodeHash = Hash(user.Id, code), CreatedAt = clock.GetUtcNow() });
        }

        audit.Record(db, "auth.mfa_recovery_codes_issued", "user", user.Id, after: AuditSnapshot.Of(new { count = CodeCount }));
        return codes;
    }

    internal async Task RevokeAllAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var active = await db.MfaRecoveryCodes.Where(c => c.UserId == userId && c.UsedAt == null && c.RevokedAt == null).ToListAsync(ct);
        foreach (var c in active)
        {
            c.RevokedAt = now;
        }
    }

    /// <summary>Consumes a matching unused code. Returns the number of codes left, or null if none matched.</summary>
    internal async Task<int?> TryConsumeAsync(User user, string? code, CancellationToken ct)
    {
        var normalized = Normalize(code);
        if (normalized.Length != 10)
        {
            return null;
        }

        var hash = Hash(user.Id, normalized);
        var match = await db.MfaRecoveryCodes.SingleOrDefaultAsync(c => c.UserId == user.Id && c.CodeHash == hash && c.UsedAt == null && c.RevokedAt == null, ct);
        if (match is null)
        {
            return null;
        }

        match.UsedAt = clock.GetUtcNow();
        var left = await db.MfaRecoveryCodes.CountAsync(c => c.UserId == user.Id && c.UsedAt == null && c.RevokedAt == null && c.Id != match.Id, ct);
        return left;
    }

    public Task<int> RemainingAsync(Guid userId, CancellationToken ct)
        => db.MfaRecoveryCodes.CountAsync(c => c.UserId == userId && c.UsedAt == null && c.RevokedAt == null, ct);

    internal async Task NotifyAsync(User user, string kind, CancellationToken ct)
    {
        var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.TenantId == context.TenantId, ct);
        audit.Enqueue(db, SecurityNoticeKinds.OutboxType, new SecurityNoticeMessage(user.Id, user.Email, user.DisplayName, tenant.Name, kind, clock.GetUtcNow()));
    }

    internal static string Normalize(string? code)
        => new((code ?? string.Empty).Trim().ToUpperInvariant().Where(c => c is not ('-' or ' ')).ToArray());

    internal static string Hash(Guid userId, string code)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId:N}|{Normalize(code)}"))).ToLowerInvariant();

    private static string NewCode()
    {
        Span<char> chars = stackalloc char[11];
        for (var i = 0; i < 11; i++)
        {
            chars[i] = i == 5 ? '-' : Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }
}
