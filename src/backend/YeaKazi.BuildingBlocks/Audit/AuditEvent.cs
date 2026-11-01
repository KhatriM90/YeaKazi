using System.Text.Json;
using YeaKazi.BuildingBlocks.Domain;

namespace YeaKazi.BuildingBlocks.Audit;

/// <summary>
/// Tenant audit record (Section 6). Append-only: the database rejects UPDATE,
/// DELETE and TRUNCATE, and a trigger assigns a per-tenant sequence and SHA-256
/// hash chain (seq, prev_hash, hash) for tamper evidence.
/// </summary>
public sealed class AuditEvent : ITenantOwned, IAuditRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid? LegalEntityId { get; set; }
    public string ActorKind { get; set; } = ActorKindsDefault;
    public Guid? ActorUserId { get; set; }
    public string? ActorDisplay { get; set; }
    public Guid? DelegatedActorUserId { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public long? EntityVersion { get; set; }
    public JsonDocument? BeforeData { get; set; }
    public JsonDocument? AfterData { get; set; }
    public string? Reason { get; set; }
    public string? CorrelationId { get; set; }
    public string? ClientIp { get; set; }

    // Assigned by the database trigger.
    public long Seq { get; set; }
    public string PrevHash { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;

    private const string ActorKindsDefault = "system";
}

/// <summary>
/// Audit record of platform operator actions (SaaS console). Platform staff have no
/// tenant, so this table is keyed by the target tenant instead (docs/assumptions.md A-03).
/// </summary>
public sealed class PlatformAuditEvent : IAuditRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid? TargetTenantId { get; set; }
    public string ActorKind { get; set; } = "system";
    public Guid? ActorPlatformUserId { get; set; }
    public string? ActorDisplay { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public JsonDocument? Details { get; set; }
    public string? Reason { get; set; }
    public string? CorrelationId { get; set; }
    public string? ClientIp { get; set; }

    public long Seq { get; set; }
    public string PrevHash { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
}
