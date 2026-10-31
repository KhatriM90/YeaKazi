using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using YeaKazi.BuildingBlocks.Audit;
using YeaKazi.BuildingBlocks.Http;
using YeaKazi.BuildingBlocks.Security;
using YeaKazi.Platform.Persistence;

namespace YeaKazi.Platform.AuditLog;

public sealed record AuditEventDto(
    Guid Id, long Seq, DateTimeOffset OccurredAt, string Action, string EntityType, string? EntityId,
    Guid? LegalEntityId, string ActorKind, Guid? ActorUserId, string? ActorDisplay, Guid? DelegatedActorUserId,
    JsonElement? Before, JsonElement? After, string? Reason, string? CorrelationId, string Hash);

public sealed record AuditSearch(
    [property: Microsoft.AspNetCore.Mvc.FromQuery(Name = "action")] string? Action,
    [property: Microsoft.AspNetCore.Mvc.FromQuery(Name = "entityType")] string? EntityType,
    [property: Microsoft.AspNetCore.Mvc.FromQuery(Name = "entityId")] string? EntityId,
    [property: Microsoft.AspNetCore.Mvc.FromQuery(Name = "actorUserId")] Guid? ActorUserId,
    [property: Microsoft.AspNetCore.Mvc.FromQuery(Name = "from")] DateTimeOffset? From,
    [property: Microsoft.AspNetCore.Mvc.FromQuery(Name = "to")] DateTimeOffset? To,
    [property: Microsoft.AspNetCore.Mvc.FromQuery(Name = "page")] int? Page,
    [property: Microsoft.AspNetCore.Mvc.FromQuery(Name = "pageSize")] int? PageSize);

public sealed record AuditChainStatus(bool Valid, long EventsChecked, long? FirstBrokenSeq);

/// <summary>Audit search (audit.read). Searching the audit log is itself audited as a sensitive read.</summary>
public sealed class AuditQueryService(PlatformDbContext db, IAccessControl access, AuditTrail audit)
{
    public async Task<PagedResult<AuditEventDto>> SearchAsync(AuditSearch search, CancellationToken ct)
    {
        var scope = await access.ScopeAsync(Permissions.AuditRead, ct);
        if (!scope.Granted)
        {
            throw new BuildingBlocks.Errors.ForbiddenException();
        }

        var (page, size) = Paging.Normalize(search.Page, search.PageSize);
        var q = db.AuditEvents.AsNoTracking();
        if (!scope.AllLegalEntities)
        {
            var ids = scope.LegalEntityIds.ToList();
            q = q.Where(e => e.LegalEntityId != null && ids.Contains(e.LegalEntityId.Value));
        }

        if (!string.IsNullOrWhiteSpace(search.Action))
        {
            q = q.Where(e => e.Action.StartsWith(search.Action.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(search.EntityType))
        {
            q = q.Where(e => e.EntityType == search.EntityType.Trim());
        }

        if (!string.IsNullOrWhiteSpace(search.EntityId))
        {
            q = q.Where(e => e.EntityId == search.EntityId.Trim());
        }

        if (search.ActorUserId is { } actor)
        {
            q = q.Where(e => e.ActorUserId == actor);
        }

        if (search.From is { } from)
        {
            q = q.Where(e => e.OccurredAt >= from);
        }

        if (search.To is { } to)
        {
            q = q.Where(e => e.OccurredAt < to);
        }

        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(e => e.Seq).Skip((page - 1) * size).Take(size).ToListAsync(ct);

        audit.Record(db, "audit.searched", "audit_event", null, after: AuditSnapshot.Of(new
        {
            search.Action, search.EntityType, search.EntityId, search.ActorUserId, search.From, search.To, page, size, total,
        }));
        await db.SaveChangesAsync(ct);

        return new PagedResult<AuditEventDto>(rows.Select(e => new AuditEventDto(
            e.Id, e.Seq, e.OccurredAt, e.Action, e.EntityType, e.EntityId, e.LegalEntityId, e.ActorKind, e.ActorUserId,
            e.ActorDisplay, e.DelegatedActorUserId, e.BeforeData?.RootElement.Clone(), e.AfterData?.RootElement.Clone(),
            e.Reason, e.CorrelationId, e.Hash)).ToList(), page, size, total);
    }

    /// <summary>Recomputes this tenant's hash chain in the database.</summary>
    public async Task<AuditChainStatus> VerifyChainAsync(CancellationToken ct)
    {
        await access.RequireAsync(Permissions.AuditRead, null, ct);
        var result = await db.Database
            .SqlQuery<ChainRow>($"select valid, events_checked, first_broken_seq from audit.verify_chain()")
            .SingleAsync(ct);
        audit.Record(db, "audit.chain_verified", "audit_event", null, after: AuditSnapshot.Of(result));
        await db.SaveChangesAsync(ct);
        return new AuditChainStatus(result.Valid, result.EventsChecked, result.FirstBrokenSeq);
    }

    private sealed class ChainRow
    {
        public bool Valid { get; set; }
        public long EventsChecked { get; set; }
        public long? FirstBrokenSeq { get; set; }
    }
}
