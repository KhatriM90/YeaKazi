using Microsoft.EntityFrameworkCore;
using YeaKazi.BuildingBlocks.Audit;
using YeaKazi.BuildingBlocks.Contracts;
using YeaKazi.BuildingBlocks.Tenancy;
using YeaKazi.Organization.Persistence;

namespace YeaKazi.Organization.Services;

/// <summary>Organization's side of the provisioning contract. Joins the caller's shared transaction.</summary>
internal sealed class OrganizationProvisioning(OrganizationDbContext db, OrganizationService service, RequestContext context, AuditTrail audit, TimeProvider clock) : IOrganizationProvisioning
{
    public async Task<Guid> CreateInitialLegalEntityAsync(InitialLegalEntity spec, CancellationToken ct = default)
    {
        if (!context.IsProvisioning)
        {
            throw new InvalidOperationException("The initial legal employer can only be created during tenant provisioning.");
        }

        db.EnlistInSharedTransaction();
        var entity = await service.CreateLegalEntityCoreAsync(spec.Code, spec.Name, spec.RegistrationNumber, spec.KraPin, spec.NssfEmployerNumber, spec.Timezone, ct);
        await db.SaveChangesAsync(ct);
        await SeedHolidayCalendarAsync(entity, ct);
        return entity.Id;
    }

    /// <summary>SAAS-01: seeds a Kenya public-holiday calendar for this year and next from the loaded rule version.</summary>
    private async Task SeedHolidayCalendarAsync(Domain.LegalEntity entity, CancellationToken ct)
    {
        var calendar = new Domain.HolidayCalendar { LegalEntityId = entity.Id, Code = "KE-PUBLIC", Name = "Kenya public holidays" };
        db.HolidayCalendars.Add(calendar);
        audit.Record(db, "org.holiday_calendar.created", "holiday_calendar", calendar.Id, after: AuditSnapshot.Of(calendar), legalEntityId: entity.Id);
        var tz = TimeZoneInfo.FindSystemTimeZoneById(entity.Timezone);
        var year = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), tz).Year;
        foreach (var y in new[] { year, year + 1 })
        {
            if (await service.LoadStatutoryCoreAsync(calendar, y, ct) is null)
            {
                audit.Record(db, "org.holiday_calendar.statutory_missing", "holiday_calendar", calendar.Id,
                    after: AuditSnapshot.Of(new { year = y, reason = "no_rule_version" }), legalEntityId: entity.Id);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}

internal sealed class OrganizationDirectory(OrganizationDbContext db) : IOrganizationDirectory
{
    public Task<bool> LegalEntityExistsAsync(Guid legalEntityId, CancellationToken ct = default)
        => db.LegalEntities.AsNoTracking().AnyAsync(x => x.Id == legalEntityId && x.IsActive, ct);
}

internal sealed class OrganizationReferences(OrganizationDbContext db) : IOrganizationReferences
{
    public async Task<LegalEntityInfo?> GetLegalEntityAsync(Guid legalEntityId, CancellationToken ct = default)
        => await db.LegalEntities.AsNoTracking().Where(x => x.Id == legalEntityId)
            .Select(x => new LegalEntityInfo(x.Id, x.Code, x.Name, x.Timezone, x.IsActive)).SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<LegalEntityInfo>> ListLegalEntitiesAsync(CancellationToken ct = default)
        => await db.LegalEntities.AsNoTracking().Select(x => new LegalEntityInfo(x.Id, x.Code, x.Name, x.Timezone, x.IsActive)).ToListAsync(ct);

    public async Task<OrgReferenceData> GetReferenceDataAsync(Guid legalEntityId, CancellationToken ct = default)
    {
        async Task<IReadOnlyList<OrgRef>> Load<T>(IQueryable<T> set) where T : Domain.EmployerRecord
            => await set.AsNoTracking().Where(x => x.LegalEntityId == legalEntityId)
                .Select(x => new OrgRef(x.Id, x.Code, x.Name, x.IsActive)).ToListAsync(ct);

        return new OrgReferenceData
        {
            Kinds = new Dictionary<string, IReadOnlyList<OrgRef>>
            {
                [OrgReferenceData.Departments] = await Load(db.Departments),
                [OrgReferenceData.Positions] = await Load(db.Positions),
                [OrgReferenceData.Branches] = await Load(db.Branches),
                [OrgReferenceData.CostCenters] = await Load(db.CostCenters),
                [OrgReferenceData.Grades] = await Load(db.Grades),
                [OrgReferenceData.WorkSchedules] = await Load(db.WorkSchedules),
            },
        };
    }
}
