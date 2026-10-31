using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using YeaKazi.BuildingBlocks.Outbox;
using YeaKazi.BuildingBlocks.Tenancy;

namespace YeaKazi.BuildingBlocks.Audit;

/// <summary>
/// Adds audit and outbox records to the same DbContext (and therefore the same
/// transaction) as the business change they describe.
/// </summary>
public sealed class AuditTrail(RequestContext context)
{
    public AuditEvent Record(
        DbContext db,
        string action,
        string entityType,
        object? entityId,
        JsonDocument? before = null,
        JsonDocument? after = null,
        string? reason = null,
        Guid? legalEntityId = null,
        long? entityVersion = null)
    {
        var tenantId = context.TenantId
            ?? throw new InvalidOperationException("Tenant audit events need a tenant context.");

        var evt = new AuditEvent
        {
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            ActorKind = context.ActorKind,
            ActorUserId = context.ActorUserId,
            ActorDisplay = context.ActorDisplay,
            DelegatedActorUserId = context.DelegatedActorUserId,
            OccurredAt = DateTimeOffset.UtcNow,
            Action = action,
            EntityType = entityType,
            EntityId = entityId?.ToString(),
            EntityVersion = entityVersion,
            BeforeData = before,
            AfterData = after,
            Reason = reason,
            CorrelationId = context.CorrelationId,
            ClientIp = context.ClientIp,
        };
        db.Add(evt);
        return evt;
    }

    public PlatformAuditEvent RecordPlatform(
        DbContext db,
        string action,
        string entityType,
        object? entityId,
        Guid? targetTenantId,
        JsonDocument? details = null,
        string? reason = null)
    {
        var evt = new PlatformAuditEvent
        {
            TargetTenantId = targetTenantId,
            ActorKind = context.ActorKind,
            ActorPlatformUserId = context.ActorKind == ActorKinds.PlatformUser ? context.ActorUserId : null,
            ActorDisplay = context.ActorDisplay,
            OccurredAt = DateTimeOffset.UtcNow,
            Action = action,
            EntityType = entityType,
            EntityId = entityId?.ToString(),
            Details = details,
            Reason = reason,
            CorrelationId = context.CorrelationId,
            ClientIp = context.ClientIp,
        };
        db.Add(evt);
        return evt;
    }

    public OutboxMessage Enqueue(DbContext db, string type, object payload)
    {
        var tenantId = context.TenantId
            ?? throw new InvalidOperationException("Outbox messages need a tenant context.");
        var msg = new OutboxMessage
        {
            TenantId = tenantId,
            Type = type,
            Payload = JsonSerializer.Serialize(payload, JsonSerializerOptions.Web),
            CorrelationId = context.CorrelationId,
        };
        db.Add(msg);
        return msg;
    }
}
