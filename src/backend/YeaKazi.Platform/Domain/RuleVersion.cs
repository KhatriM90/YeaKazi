using System.Text.Json;

namespace YeaKazi.Platform.Domain;

/// <summary>
/// Effective-dated statutory rule data (Section 13), loaded from /rules by the schema owner.
/// Product-wide, so it has no tenant_id (docs/assumptions.md A-17). The API role can only read it.
/// </summary>
public sealed class RuleVersion
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Jurisdiction { get; set; } = string.Empty;
    public string RuleCode { get; set; } = string.Empty;
    public string VersionLabel { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string AuthoritySource { get; set; } = string.Empty;
    public string? Applicability { get; set; }
    public string? Reviewer { get; set; }
    public DateOnly? ApprovedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public JsonDocument Payload { get; set; } = JsonDocument.Parse("{}");
    public string ContentHash { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;
    public DateTimeOffset LoadedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Single-use MFA recovery code. Only a SHA-256 hash of the code is stored.</summary>
public sealed class MfaRecoveryCode : YeaKazi.BuildingBlocks.Domain.ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    [YeaKazi.BuildingBlocks.Domain.AuditIgnore] public string CodeHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
