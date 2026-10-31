using Microsoft.EntityFrameworkCore;
using YeaKazi.BuildingBlocks.Audit;
using YeaKazi.BuildingBlocks.Errors;
using YeaKazi.BuildingBlocks.Http;
using YeaKazi.BuildingBlocks.Security;
using YeaKazi.Organization.Domain;
using YeaKazi.Organization.Persistence;

namespace YeaKazi.Organization.Services;

/// <summary>
/// ORG-01 organization setup. Every change is permission-checked against the employer it
/// touches, version-checked (optimistic concurrency) and audited in the same transaction.
/// Records are deactivated, never deleted, so history and references stay intact.
/// </summary>
public sealed partial class OrganizationService(OrganizationDbContext db, IAccessControl access, AuditTrail audit)
{
    public const string HolidayChangedEvent = "org.holiday_changed";

    // ---------- Legal employers ----------

    public async Task<PagedResult<LegalEntityDto>> ListLegalEntitiesAsync(OrgListQuery q, CancellationToken ct)
    {
        var scope = await ReadScopeAsync(ct);
        var (page, size) = Paging.Normalize(q.Page, q.PageSize);
        var query = db.LegalEntities.AsNoTracking();
        if (!scope.AllLegalEntities)
        {
            var ids = scope.LegalEntityIds.ToList();
            query = query.Where(x => ids.Contains(x.Id));
        }

        if (q.IncludeInactive != true)
        {
            query = query.Where(x => x.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = $"%{q.Search.Trim()}%";
            query = query.Where(x => EF.Functions.ILike(x.Name, term) || EF.Functions.ILike(x.Code, term));
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Code).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<LegalEntityDto>(rows.Select(Map).ToList(), page, size, total);
    }

    public async Task<LegalEntityDto> GetLegalEntityAsync(Guid id, CancellationToken ct)
    {
        var scope = await ReadScopeAsync(ct);
        var entity = await db.LegalEntities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null || !scope.Allows(entity.Id))
        {
            throw new NotFoundException("Legal employer");
        }

        return Map(entity);
    }

    public async Task<LegalEntityDto> CreateLegalEntityAsync(CreateLegalEntityRequest r, CancellationToken ct)
    {
        // A new employer is tenant-wide configuration: requires org.write for all employers.
        var scope = await access.ScopeAsync(Permissions.OrgWrite, ct);
        if (!scope.AllLegalEntities)
        {
            throw new ForbiddenException();
        }

        var entity = await CreateLegalEntityCoreAsync(r.Code, r.Name, r.RegistrationNumber, r.KraPin, r.NssfEmployerNumber, r.Timezone, ct);
        await db.SaveChangesAsync(ct);
        return Map(entity);
    }

    internal async Task<LegalEntity> CreateLegalEntityCoreAsync(string code, string name, string? registrationNumber, string? kraPin, string? nssf, string? timezone, CancellationToken ct)
    {
        var normalizedCode = code?.Trim().ToUpperInvariant() ?? string.Empty;
        var pin = Clean(kraPin)?.ToUpperInvariant();
        var tz = Clean(timezone) ?? "Africa/Nairobi";
        new Validator()
            .Required("code", normalizedCode, 20)
            .Must(CodeOk(normalizedCode), "code", "Use letters, digits, hyphens or underscores.")
            .Required("name", name, 200)
            .Optional("registrationNumber", registrationNumber, 50)
            .Must(pin is null || KenyanIdentifiers.IsValidEmployerKraPin(pin), "kraPin", "Enter a valid KRA PIN, for example P051234567X.")
            .Optional("nssfEmployerNumber", nssf, 20)
            .Must(TimezoneOk(tz), "timezone", "Unknown time zone.")
            .ThrowIfInvalid();

        if (await db.LegalEntities.AnyAsync(x => x.Code == normalizedCode, ct))
        {
            throw new ConflictException("duplicate_code", "A legal employer with this code already exists.");
        }

        if (pin is not null && await db.LegalEntities.AnyAsync(x => x.KraPin == pin, ct))
        {
            throw new ConflictException("duplicate_kra_pin", "Another legal employer already uses this KRA PIN.");
        }

        var entity = new LegalEntity
        {
            Code = normalizedCode,
            Name = name.Trim(),
            RegistrationNumber = Clean(registrationNumber),
            KraPin = pin,
            NssfEmployerNumber = Clean(nssf),
            Timezone = tz,
            BaseCurrency = "KES",
        };
        db.LegalEntities.Add(entity);
        audit.Record(db, "org.legal_entity.created", "legal_entity", entity.Id, after: AuditSnapshot.Of(entity), legalEntityId: entity.Id);
        return entity;
    }

    public async Task<LegalEntityDto> UpdateLegalEntityAsync(Guid id, UpdateLegalEntityRequest r, CancellationToken ct)
    {
        var scope = await ReadScopeAsync(ct);
        var entity = await db.LegalEntities.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null || !scope.Allows(entity.Id))
        {
            throw new NotFoundException("Legal employer");
        }

        await access.RequireAsync(Permissions.OrgWrite, entity.Id, ct);
        CheckVersion(entity, entity.Version, r.Version);
        var pin = Clean(r.KraPin)?.ToUpperInvariant();
        new Validator()
            .Required("name", r.Name, 200)
            .Optional("registrationNumber", r.RegistrationNumber, 50)
            .Must(pin is null || KenyanIdentifiers.IsValidEmployerKraPin(pin), "kraPin", "Enter a valid KRA PIN, for example P051234567X.")
            .Optional("nssfEmployerNumber", r.NssfEmployerNumber, 20)
            .Must(TimezoneOk(r.Timezone), "timezone", "Unknown time zone.")
            .Optional("reason", r.Reason, 1000)
            .ThrowIfInvalid();

        if (pin is not null && await db.LegalEntities.AnyAsync(x => x.KraPin == pin && x.Id != id, ct))
        {
            throw new ConflictException("duplicate_kra_pin", "Another legal employer already uses this KRA PIN.");
        }

        if (!r.IsActive && entity.IsActive && !await db.LegalEntities.AnyAsync(x => x.IsActive && x.Id != id, ct))
        {
            throw new BusinessRuleException("last_legal_entity", "A tenant must keep at least one active legal employer.");
        }

        var before = AuditSnapshot.Of(entity);
        var wasActive = entity.IsActive;
        entity.Name = r.Name.Trim();
        entity.RegistrationNumber = Clean(r.RegistrationNumber);
        entity.KraPin = pin;
        entity.NssfEmployerNumber = Clean(r.NssfEmployerNumber);
        entity.Timezone = r.Timezone.Trim();
        entity.IsActive = r.IsActive;
        audit.Record(db, ActionFor("legal_entity", wasActive, entity.IsActive), "legal_entity", entity.Id, before, AuditSnapshot.Of(entity), r.Reason, entity.Id);
        await db.SaveChangesAsync(ct);
        return Map(entity);
    }

    // ---------- Branches ----------

    public Task<PagedResult<BranchDto>> ListBranchesAsync(OrgListQuery q, CancellationToken ct) => ListAsync<Branch, BranchDto>(q, Map, ct);

    public async Task<BranchDto> GetBranchAsync(Guid id, CancellationToken ct) => Map(await GetAsync<Branch>(id, ct));

    public async Task<BranchDto> CreateBranchAsync(CreateBranchRequest r, CancellationToken ct)
    {
        var v = new Validator().Optional("address", r.Address, 400);
        var e = await CreateAsync<Branch>(r.LegalEntityId, r.Code, r.Name, v, x => x.Address = Clean(r.Address), ct);
        return Map(e);
    }

    public async Task<BranchDto> UpdateBranchAsync(Guid id, UpdateBranchRequest r, CancellationToken ct)
    {
        var e = await UpdateAsync<Branch>(id, r.Version, r.Name, r.IsActive, r.Reason,
            new Validator().Optional("address", r.Address, 400),
            (x, _) =>
            {
                x.Address = Clean(r.Address);
                return Task.CompletedTask;
            }, ct);
        return Map(e);
    }

    // ---------- Cost centers ----------

    public Task<PagedResult<CostCenterDto>> ListCostCentersAsync(OrgListQuery q, CancellationToken ct) => ListAsync<CostCenter, CostCenterDto>(q, Map, ct);

    public async Task<CostCenterDto> GetCostCenterAsync(Guid id, CancellationToken ct) => Map(await GetAsync<CostCenter>(id, ct));

    public async Task<CostCenterDto> CreateCostCenterAsync(CreateCostCenterRequest r, CancellationToken ct)
        => Map(await CreateAsync<CostCenter>(r.LegalEntityId, r.Code, r.Name, new Validator(), _ => { }, ct));

    public async Task<CostCenterDto> UpdateCostCenterAsync(Guid id, UpdateCostCenterRequest r, CancellationToken ct)
        => Map(await UpdateAsync<CostCenter>(id, r.Version, r.Name, r.IsActive, r.Reason, new Validator(), async (x, ct2) =>
        {
            if (!r.IsActive && x.IsActive && await db.Departments.AnyAsync(d => d.CostCenterId == x.Id && d.IsActive, ct2))
            {
                throw new BusinessRuleException("in_use", "Active departments still use this cost center.");
            }
        }, ct));

    // ---------- Grades ----------

    public Task<PagedResult<GradeDto>> ListGradesAsync(OrgListQuery q, CancellationToken ct) => ListAsync<Grade, GradeDto>(q, Map, ct);

    public async Task<GradeDto> GetGradeAsync(Guid id, CancellationToken ct) => Map(await GetAsync<Grade>(id, ct));

    public async Task<GradeDto> CreateGradeAsync(CreateGradeRequest r, CancellationToken ct)
        => Map(await CreateAsync<Grade>(r.LegalEntityId, r.Code, r.Name,
            new Validator().Must(r.Rank is >= 1 and <= 1000, "rank", "Rank must be between 1 and 1000."),
            x => x.Rank = r.Rank, ct));

    public async Task<GradeDto> UpdateGradeAsync(Guid id, UpdateGradeRequest r, CancellationToken ct)
        => Map(await UpdateAsync<Grade>(id, r.Version, r.Name, r.IsActive, r.Reason,
            new Validator().Must(r.Rank is >= 1 and <= 1000, "rank", "Rank must be between 1 and 1000."),
            (x, _) =>
            {
                x.Rank = r.Rank;
                return Task.CompletedTask;
            }, ct));

    // ---------- Departments ----------

    public Task<PagedResult<DepartmentDto>> ListDepartmentsAsync(OrgListQuery q, CancellationToken ct) => ListAsync<Department, DepartmentDto>(q, Map, ct);

    public async Task<DepartmentDto> GetDepartmentAsync(Guid id, CancellationToken ct) => Map(await GetAsync<Department>(id, ct));

    public async Task<DepartmentDto> CreateDepartmentAsync(CreateDepartmentRequest r, CancellationToken ct)
    {
        var v = new Validator();
        await EnsureReferenceAsync(db.Departments, r.ParentDepartmentId, r.LegalEntityId, "parentDepartmentId", v, ct);
        await EnsureReferenceAsync(db.CostCenters, r.CostCenterId, r.LegalEntityId, "costCenterId", v, ct);
        return Map(await CreateAsync<Department>(r.LegalEntityId, r.Code, r.Name, v, x =>
        {
            x.ParentDepartmentId = r.ParentDepartmentId;
            x.CostCenterId = r.CostCenterId;
        }, ct));
    }

    public async Task<DepartmentDto> UpdateDepartmentAsync(Guid id, UpdateDepartmentRequest r, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var e = await UpdateAsync<Department>(id, r.Version, r.Name, r.IsActive, r.Reason, new Validator(), async (x, ct2) =>
        {
            var v = new Validator();
            await EnsureReferenceAsync(db.Departments, r.ParentDepartmentId, x.LegalEntityId, "parentDepartmentId", v, ct2);
            await EnsureReferenceAsync(db.CostCenters, r.CostCenterId, x.LegalEntityId, "costCenterId", v, ct2);
            v.ThrowIfInvalid();
            if (r.ParentDepartmentId != x.ParentDepartmentId)
            {
                await LockHierarchyAsync("departments", x.LegalEntityId, ct2);
                var parents = await db.Departments.AsNoTracking().Where(d => d.LegalEntityId == x.LegalEntityId)
                    .ToDictionaryAsync(d => d.Id, d => d.ParentDepartmentId, ct2);
                if (Hierarchy.WouldCreateCycle(x.Id, r.ParentDepartmentId, parents))
                {
                    throw new BusinessRuleException("circular_hierarchy", "This parent would create a circular department structure.");
                }
            }

            if (!r.IsActive && x.IsActive)
            {
                if (await db.Departments.AnyAsync(d => d.ParentDepartmentId == x.Id && d.IsActive, ct2)
                    || await db.Positions.AnyAsync(p => p.DepartmentId == x.Id && p.IsActive, ct2))
                {
                    throw new BusinessRuleException("in_use", "Move or deactivate the active sub-departments and positions first.");
                }
            }

            x.ParentDepartmentId = r.ParentDepartmentId;
            x.CostCenterId = r.CostCenterId;
        }, ct);
        await tx.CommitAsync(ct);
        return Map(e);
    }

    // ---------- Positions and reporting lines ----------

    public Task<PagedResult<PositionDto>> ListPositionsAsync(OrgListQuery q, CancellationToken ct) => ListAsync<Position, PositionDto>(q, Map, ct);

    public async Task<PositionDto> GetPositionAsync(Guid id, CancellationToken ct) => Map(await GetAsync<Position>(id, ct));

    public async Task<PositionDto> CreatePositionAsync(CreatePositionRequest r, CancellationToken ct)
    {
        var v = new Validator();
        await EnsureReferenceAsync(db.Departments, r.DepartmentId, r.LegalEntityId, "departmentId", v, ct);
        await EnsureReferenceAsync(db.Grades, r.GradeId, r.LegalEntityId, "gradeId", v, ct);
        await EnsureReferenceAsync(db.Branches, r.BranchId, r.LegalEntityId, "branchId", v, ct);
        await EnsureReferenceAsync(db.Positions, r.ReportsToPositionId, r.LegalEntityId, "reportsToPositionId", v, ct);
        return Map(await CreateAsync<Position>(r.LegalEntityId, r.Code, r.Name, v, x =>
        {
            x.DepartmentId = r.DepartmentId;
            x.GradeId = r.GradeId;
            x.BranchId = r.BranchId;
            x.ReportsToPositionId = r.ReportsToPositionId;
        }, ct));
    }

    public async Task<PositionDto> UpdatePositionAsync(Guid id, UpdatePositionRequest r, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var e = await UpdateAsync<Position>(id, r.Version, r.Name, r.IsActive, r.Reason, new Validator(), async (x, ct2) =>
        {
            var v = new Validator();
            await EnsureReferenceAsync(db.Departments, r.DepartmentId, x.LegalEntityId, "departmentId", v, ct2);
            await EnsureReferenceAsync(db.Grades, r.GradeId, x.LegalEntityId, "gradeId", v, ct2);
            await EnsureReferenceAsync(db.Branches, r.BranchId, x.LegalEntityId, "branchId", v, ct2);
            await EnsureReferenceAsync(db.Positions, r.ReportsToPositionId, x.LegalEntityId, "reportsToPositionId", v, ct2);
            v.ThrowIfInvalid();
            if (r.ReportsToPositionId != x.ReportsToPositionId)
            {
                await LockHierarchyAsync("positions", x.LegalEntityId, ct2);
                var managers = await db.Positions.AsNoTracking().Where(p => p.LegalEntityId == x.LegalEntityId)
                    .ToDictionaryAsync(p => p.Id, p => p.ReportsToPositionId, ct2);
                if (Hierarchy.WouldCreateCycle(x.Id, r.ReportsToPositionId, managers))
                {
                    throw new BusinessRuleException("circular_reporting_line", "This reporting line would be circular.");
                }
            }

            if (!r.IsActive && x.IsActive && await db.Positions.AnyAsync(p => p.ReportsToPositionId == x.Id && p.IsActive, ct2))
            {
                throw new BusinessRuleException("in_use", "Re-assign the positions that report to this position first.");
            }

            x.DepartmentId = r.DepartmentId;
            x.GradeId = r.GradeId;
            x.BranchId = r.BranchId;
            x.ReportsToPositionId = r.ReportsToPositionId;
        }, ct);
        await tx.CommitAsync(ct);
        return Map(e);
    }

    // ---------- Holiday calendars ----------

    public Task<PagedResult<HolidayCalendarDto>> ListHolidayCalendarsAsync(OrgListQuery q, CancellationToken ct) => ListAsync<HolidayCalendar, HolidayCalendarDto>(q, Map, ct);

    public async Task<HolidayCalendarDto> GetHolidayCalendarAsync(Guid id, CancellationToken ct) => Map(await GetAsync<HolidayCalendar>(id, ct));

    public async Task<HolidayCalendarDto> CreateHolidayCalendarAsync(CreateHolidayCalendarRequest r, CancellationToken ct)
        => Map(await CreateAsync<HolidayCalendar>(r.LegalEntityId, r.Code, r.Name, new Validator(), _ => { }, ct));

    public async Task<HolidayCalendarDto> UpdateHolidayCalendarAsync(Guid id, UpdateHolidayCalendarRequest r, CancellationToken ct)
        => Map(await UpdateAsync<HolidayCalendar>(id, r.Version, r.Name, r.IsActive, r.Reason, new Validator(), (_, _) => Task.CompletedTask, ct));

    public async Task<IReadOnlyList<HolidayDto>> ListHolidaysAsync(Guid calendarId, int? year, bool? includeInactive, CancellationToken ct)
    {
        var calendar = await GetAsync<HolidayCalendar>(calendarId, ct);
        var q = db.Holidays.AsNoTracking().Where(h => h.CalendarId == calendar.Id);
        if (includeInactive != true)
        {
            q = q.Where(h => h.IsActive);
        }

        if (year is { } y)
        {
            var from = new DateOnly(y, 1, 1);
            var to = new DateOnly(y, 12, 31);
            q = q.Where(h => h.Date >= from && h.Date <= to);
        }

        return (await q.OrderBy(h => h.Date).ToListAsync(ct)).Select(Map).ToList();
    }

    /// <summary>
    /// Adds a holiday. Holiday dates are entered by administrators: no gazetted dates are
    /// seeded until an approved source is loaded (docs/assumptions.md A-08). A change emits
    /// org.holiday_changed so later slices can recalculate unlocked periods only (KEN-15).
    /// </summary>
    public async Task<HolidayDto> AddHolidayAsync(Guid calendarId, CreateHolidayRequest r, CancellationToken ct)
    {
        var calendar = await GetAsync<HolidayCalendar>(calendarId, ct);
        await access.RequireAsync(Permissions.OrgWrite, calendar.LegalEntityId, ct);
        new Validator()
            .Required("name", r.Name, 200)
            .Must(HolidayKinds.All.Contains(r.Kind ?? string.Empty), "kind", "Kind must be gazetted, short_notice or company.")
            .Must(r.Date.Year is >= 2000 and <= 2100, "date", "Enter a valid date.")
            .ThrowIfInvalid();
        if (!calendar.IsActive)
        {
            throw new BusinessRuleException("inactive_calendar", "The calendar is inactive.");
        }

        if (await db.Holidays.AnyAsync(h => h.CalendarId == calendarId && h.Date == r.Date && h.IsActive, ct))
        {
            throw new ConflictException("duplicate_holiday", "This calendar already has a holiday on that date.");
        }

        var holiday = new Holiday { LegalEntityId = calendar.LegalEntityId, CalendarId = calendarId, Date = r.Date, Name = r.Name.Trim(), Kind = r.Kind! };
        db.Holidays.Add(holiday);
        audit.Record(db, "org.holiday.created", "holiday", holiday.Id, after: AuditSnapshot.Of(holiday), legalEntityId: calendar.LegalEntityId);
        audit.Enqueue(db, HolidayChangedEvent, new { holiday.LegalEntityId, holiday.CalendarId, holiday.Date, change = "added" });
        await db.SaveChangesAsync(ct);
        return Map(holiday);
    }

    public async Task<HolidayDto> UpdateHolidayAsync(Guid calendarId, Guid holidayId, UpdateHolidayRequest r, CancellationToken ct)
    {
        var calendar = await GetAsync<HolidayCalendar>(calendarId, ct);
        await access.RequireAsync(Permissions.OrgWrite, calendar.LegalEntityId, ct);
        var holiday = await db.Holidays.SingleOrDefaultAsync(h => h.Id == holidayId && h.CalendarId == calendarId, ct)
            ?? throw new NotFoundException("Holiday");
        CheckVersion(holiday, holiday.Version, r.Version);
        new Validator()
            .Required("name", r.Name, 200)
            .Must(HolidayKinds.All.Contains(r.Kind ?? string.Empty), "kind", "Kind must be gazetted, short_notice or company.")
            .Optional("reason", r.Reason, 1000)
            .ThrowIfInvalid();
        if (r.IsActive && !holiday.IsActive
            && await db.Holidays.AnyAsync(h => h.CalendarId == calendarId && h.Date == holiday.Date && h.IsActive && h.Id != holidayId, ct))
        {
            throw new ConflictException("duplicate_holiday", "This calendar already has a holiday on that date.");
        }

        var before = AuditSnapshot.Of(holiday);
        var wasActive = holiday.IsActive;
        holiday.Name = r.Name.Trim();
        holiday.Kind = r.Kind!;
        holiday.IsActive = r.IsActive;
        audit.Record(db, ActionFor("holiday", wasActive, holiday.IsActive), "holiday", holiday.Id, before, AuditSnapshot.Of(holiday), r.Reason, calendar.LegalEntityId);
        if (wasActive != holiday.IsActive)
        {
            audit.Enqueue(db, HolidayChangedEvent, new { holiday.LegalEntityId, holiday.CalendarId, holiday.Date, change = holiday.IsActive ? "reactivated" : "deactivated" });
        }

        await db.SaveChangesAsync(ct);
        return Map(holiday);
    }

    // ---------- Work schedules ----------

    public Task<PagedResult<WorkScheduleDto>> ListWorkSchedulesAsync(OrgListQuery q, CancellationToken ct) => ListAsync<WorkSchedule, WorkScheduleDto>(q, Map, ct);

    public async Task<WorkScheduleDto> GetWorkScheduleAsync(Guid id, CancellationToken ct) => Map(await GetAsync<WorkSchedule>(id, ct));

    public async Task<WorkScheduleDto> CreateWorkScheduleAsync(CreateWorkScheduleRequest r, CancellationToken ct)
    {
        var (start, end, v) = ValidateSchedule(r.StartTime, r.EndTime, r.BreakMinutes, r.WorkingDays);
        return Map(await CreateAsync<WorkSchedule>(r.LegalEntityId, r.Code, r.Name, v, x =>
        {
            x.StartTime = start;
            x.EndTime = end;
            x.BreakMinutes = r.BreakMinutes;
            x.WorkingDays = r.WorkingDays.Distinct().Order().ToArray();
        }, ct));
    }

    public async Task<WorkScheduleDto> UpdateWorkScheduleAsync(Guid id, UpdateWorkScheduleRequest r, CancellationToken ct)
    {
        var (start, end, v) = ValidateSchedule(r.StartTime, r.EndTime, r.BreakMinutes, r.WorkingDays);
        return Map(await UpdateAsync<WorkSchedule>(id, r.Version, r.Name, r.IsActive, r.Reason, v, (x, _) =>
        {
            x.StartTime = start;
            x.EndTime = end;
            x.BreakMinutes = r.BreakMinutes;
            x.WorkingDays = r.WorkingDays.Distinct().Order().ToArray();
            return Task.CompletedTask;
        }, ct));
    }

    internal static (TimeOnly Start, TimeOnly End, Validator V) ValidateSchedule(string? startText, string? endText, int breakMinutes, IReadOnlyList<int>? days)
    {
        var v = new Validator();
        var okStart = TimeOnly.TryParseExact(startText ?? string.Empty, "HH:mm", out var start);
        var okEnd = TimeOnly.TryParseExact(endText ?? string.Empty, "HH:mm", out var end);
        v.Must(okStart, "startTime", "Use 24-hour HH:mm.");
        v.Must(okEnd, "endTime", "Use 24-hour HH:mm.");
        v.Must(days is { Count: > 0 } && days.All(d => d is >= 1 and <= 7), "workingDays", "Choose working days (1 = Monday ... 7 = Sunday).");
        if (okStart && okEnd)
        {
            v.Must(start != end, "endTime", "End time must differ from start time.");
            var minutes = (int)(end.ToTimeSpan() - start.ToTimeSpan()).TotalMinutes;
            if (minutes <= 0)
            {
                minutes += 24 * 60;
            }

            v.Must(breakMinutes >= 0 && breakMinutes < minutes, "breakMinutes", "Break must be shorter than the shift.");
        }

        return (start, end, v);
    }

    // ---------- Shared helpers ----------

    private async Task<PermissionScope> ReadScopeAsync(CancellationToken ct)
    {
        var scope = await access.ScopeAsync(Permissions.OrgRead, ct);
        if (!scope.Granted)
        {
            throw new ForbiddenException();
        }

        return scope;
    }

    private async Task<PagedResult<TDto>> ListAsync<T, TDto>(OrgListQuery q, Func<T, TDto> map, CancellationToken ct)
        where T : EmployerRecord
    {
        var scope = await ReadScopeAsync(ct);
        var (page, size) = Paging.Normalize(q.Page, q.PageSize);
        IQueryable<T> query = db.Set<T>().AsNoTracking();
        if (!scope.AllLegalEntities)
        {
            var ids = scope.LegalEntityIds.ToList();
            query = query.Where(x => ids.Contains(x.LegalEntityId));
        }

        if (q.LegalEntityId is { } le)
        {
            query = query.Where(x => x.LegalEntityId == le);
        }

        if (q.IncludeInactive != true)
        {
            query = query.Where(x => x.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = $"%{q.Search.Trim()}%";
            query = query.Where(x => EF.Functions.ILike(x.Name, term) || EF.Functions.ILike(x.Code, term));
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Code).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<TDto>(rows.Select(map).ToList(), page, size, total);
    }

    private async Task<T> GetAsync<T>(Guid id, CancellationToken ct)
        where T : EmployerRecord
    {
        var scope = await ReadScopeAsync(ct);
        var entity = await db.Set<T>().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null || !scope.Allows(entity.LegalEntityId))
        {
            throw new NotFoundException(Label<T>());
        }

        return entity;
    }

    private async Task<T> CreateAsync<T>(Guid legalEntityId, string code, string name, Validator v, Action<T> apply, CancellationToken ct)
        where T : EmployerRecord, new()
    {
        await access.RequireAsync(Permissions.OrgWrite, legalEntityId, ct);
        var normalizedCode = code?.Trim().ToUpperInvariant() ?? string.Empty;
        v.Required("code", normalizedCode, 20)
            .Must(CodeOk(normalizedCode), "code", "Use letters, digits, hyphens or underscores.")
            .Required("name", name, 200);
        var le = await db.LegalEntities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == legalEntityId, ct);
        v.Must(le is { IsActive: true }, "legalEntityId", "Choose an active legal employer.");
        v.ThrowIfInvalid();

        if (await db.Set<T>().AnyAsync(x => x.LegalEntityId == legalEntityId && x.Code == normalizedCode, ct))
        {
            throw new ConflictException("duplicate_code", $"A {Label<T>().ToLowerInvariant()} with this code already exists for this employer.");
        }

        var entity = new T { LegalEntityId = legalEntityId, Code = normalizedCode, Name = name.Trim() };
        apply(entity);
        db.Set<T>().Add(entity);
        audit.Record(db, $"org.{Kind<T>()}.created", Kind<T>(), entity.Id, after: AuditSnapshot.Of(entity), legalEntityId: legalEntityId);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    private async Task<T> UpdateAsync<T>(Guid id, uint version, string name, bool isActive, string? reason, Validator v, Func<T, CancellationToken, Task> apply, CancellationToken ct)
        where T : EmployerRecord
    {
        var entity = await GetAsync<T>(id, ct);
        await access.RequireAsync(Permissions.OrgWrite, entity.LegalEntityId, ct);
        CheckVersion(entity, entity.Version, version);
        v.Required("name", name, 200).Optional("reason", reason, 1000).ThrowIfInvalid();

        var before = AuditSnapshot.Of(entity);
        var wasActive = entity.IsActive;
        await apply(entity, ct);
        entity.Name = name.Trim();
        entity.IsActive = isActive;
        audit.Record(db, $"org.{Kind<T>()}.{(wasActive == isActive ? "updated" : isActive ? "reactivated" : "deactivated")}",
            Kind<T>(), entity.Id, before, AuditSnapshot.Of(entity), reason, entity.LegalEntityId);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    private static async Task EnsureReferenceAsync<T>(DbSet<T> set, Guid? id, Guid legalEntityId, string field, Validator v, CancellationToken ct)
        where T : EmployerRecord
    {
        if (id is null)
        {
            return;
        }

        var ok = await set.AnyAsync(x => x.Id == id && x.LegalEntityId == legalEntityId && x.IsActive, ct);
        v.Must(ok, field, "Choose an active record of the same legal employer.");
    }

    private Task LockHierarchyAsync(string hierarchy, Guid legalEntityId, CancellationToken ct)
        => db.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtextextended({"org." + hierarchy + ":" + legalEntityId}, 0))", ct);

    private void CheckVersion(object entity, uint current, uint expected)
    {
        if (current != expected)
        {
            throw new ConflictException("concurrency_conflict", "The record was changed by someone else. Reload and try again.");
        }

        db.Entry(entity).Property(nameof(YeaKazi.BuildingBlocks.Domain.IVersioned.Version)).OriginalValue = expected;
    }

    private static string ActionFor(string kind, bool wasActive, bool isActive)
        => $"org.{kind}.{(wasActive == isActive ? "updated" : isActive ? "reactivated" : "deactivated")}";

    private static string Kind<T>() => typeof(T).Name switch
    {
        nameof(Branch) => "branch",
        nameof(CostCenter) => "cost_center",
        nameof(Grade) => "grade",
        nameof(Department) => "department",
        nameof(Position) => "position",
        nameof(HolidayCalendar) => "holiday_calendar",
        nameof(WorkSchedule) => "work_schedule",
        _ => typeof(T).Name.ToLowerInvariant(),
    };

    private static string Label<T>() => typeof(T).Name switch
    {
        nameof(CostCenter) => "Cost center",
        nameof(HolidayCalendar) => "Holiday calendar",
        nameof(WorkSchedule) => "Work schedule",
        _ => typeof(T).Name,
    };

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static bool CodeOk(string code) => code.Length > 0 && code.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static bool TimezoneOk(string? tz) => !string.IsNullOrWhiteSpace(tz) && TimeZoneInfo.TryFindSystemTimeZoneById(tz.Trim(), out _);

    private static LegalEntityDto Map(LegalEntity x) => new(x.Id, x.Code, x.Name, x.RegistrationNumber, x.KraPin, x.NssfEmployerNumber, x.Timezone, x.BaseCurrency, x.IsActive, x.Version);
    private static BranchDto Map(Branch x) => new(x.Id, x.LegalEntityId, x.Code, x.Name, x.Address, x.IsActive, x.Version);
    private static CostCenterDto Map(CostCenter x) => new(x.Id, x.LegalEntityId, x.Code, x.Name, x.IsActive, x.Version);
    private static GradeDto Map(Grade x) => new(x.Id, x.LegalEntityId, x.Code, x.Name, x.Rank, x.IsActive, x.Version);
    private static DepartmentDto Map(Department x) => new(x.Id, x.LegalEntityId, x.Code, x.Name, x.ParentDepartmentId, x.CostCenterId, x.IsActive, x.Version);
    private static PositionDto Map(Position x) => new(x.Id, x.LegalEntityId, x.Code, x.Name, x.DepartmentId, x.GradeId, x.BranchId, x.ReportsToPositionId, x.IsActive, x.Version);
    private static HolidayCalendarDto Map(HolidayCalendar x) => new(x.Id, x.LegalEntityId, x.Code, x.Name, x.IsActive, x.Version);
    private static HolidayDto Map(Holiday x) => new(x.Id, x.CalendarId, x.Date, x.Name, x.Kind, x.IsActive, x.Version);
    private static WorkScheduleDto Map(WorkSchedule x) => new(x.Id, x.LegalEntityId, x.Code, x.Name, x.StartTime.ToString("HH:mm"), x.EndTime.ToString("HH:mm"), x.BreakMinutes, x.WorkingDays, x.IsOvernight, x.ScheduledMinutesPerDay, x.IsActive, x.Version);
}
