using Microsoft.EntityFrameworkCore;
using YeaKazi.BuildingBlocks.Audit;
using YeaKazi.BuildingBlocks.Contracts;
using YeaKazi.BuildingBlocks.Errors;
using YeaKazi.BuildingBlocks.Http;
using YeaKazi.BuildingBlocks.Security;
using YeaKazi.BuildingBlocks.Tenancy;
using YeaKazi.People.Domain;
using YeaKazi.People.Persistence;

namespace YeaKazi.People.Services;

/// <summary>
/// Employee master and employment history (EMP-01 to EMP-04). Every write is permission-checked
/// against the employer, version-checked and audited in the same transaction. History is never
/// overwritten: new versions are added, mistakes are cancelled and corrected, and rehire opens a
/// new episode.
/// </summary>
public sealed class EmployeeService(
    PeopleDbContext db,
    IAccessControl access,
    AuditTrail audit,
    IOrganizationReferences org,
    RequestContext context,
    TimeProvider clock)
{
    public const string EmployeeChangedEvent = "people.employee_changed";
    private readonly Dictionary<Guid, DateOnly> _today = new();

    // ---------- Queries ----------

    public async Task<PagedResult<EmployeeSummaryDto>> ListAsync(EmployeeListQuery q, CancellationToken ct)
    {
        var scope = await ReadScopeAsync(ct);
        var (page, size) = Paging.Normalize(q.Page, q.PageSize);
        var query = db.Employees.AsNoTracking();
        if (!scope.AllLegalEntities)
        {
            var ids = scope.LegalEntityIds.ToList();
            query = query.Where(e => ids.Contains(e.LegalEntityId));
        }

        if (q.LegalEntityId is { } le)
        {
            query = query.Where(e => e.LegalEntityId == le);
        }

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = $"%{q.Search.Trim()}%";
            query = query.Where(e => EF.Functions.ILike(e.EmployeeNumber, term) || EF.Functions.ILike(e.FirstName, term)
                                     || EF.Functions.ILike(e.LastName, term) || EF.Functions.ILike((e.FirstName + " " + e.LastName), term));
        }

        if (q.Status is not null && !EmployeeStatuses.All.Contains(q.Status))
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["status"] = ["Use pending_start, active, suspended or terminated."] });
        }

        query = query.OrderBy(e => e.LastName).ThenBy(e => e.FirstName).ThenBy(e => e.EmployeeNumber);
        var filterInMemory = q.Status is not null || q.DepartmentId is not null;
        var employees = filterInMemory
            ? await query.ToListAsync(ct)
            : await query.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var total = filterInMemory ? 0 : await query.CountAsync(ct);

        var ids2 = employees.Select(e => e.Id).ToList();
        var episodes = (await db.Episodes.AsNoTracking().Where(x => ids2.Contains(x.EmployeeId)).ToListAsync(ct)).ToLookup(x => x.EmployeeId);
        var versions = (await db.Assignments.AsNoTracking().Where(x => ids2.Contains(x.EmployeeId) && !x.IsCancelled).ToListAsync(ct)).ToLookup(x => x.EmployeeId);

        var rows = new List<EmployeeSummaryDto>();
        foreach (var e in employees)
        {
            var today = await TodayAsync(e.LegalEntityId, ct);
            var eps = episodes[e.Id].ToList();
            var vs = versions[e.Id].ToList();
            var status = History.StatusOn(eps, vs, today);
            var episode = History.RelevantEpisode(eps, today);
            var current = episode is null ? null : History.AssignmentOn(vs, episode.Id, today < episode.HireDate ? episode.HireDate : today);
            if (q.Status is not null && status != q.Status)
            {
                continue;
            }

            if (q.DepartmentId is not null && current?.DepartmentId != q.DepartmentId)
            {
                continue;
            }

            rows.Add(new EmployeeSummaryDto(e.Id, e.LegalEntityId, e.EmployeeNumber, e.FullName, status, current?.JobTitle, current?.WorkerCategory,
                current?.DepartmentId, current?.PositionId, current?.BranchId, episode?.HireDate, episode?.TerminationDate,
                EmployeeRules.Gaps(e, current?.WorkerCategory, today)));
        }

        if (filterInMemory)
        {
            total = rows.Count;
            rows = rows.Skip((page - 1) * size).Take(size).ToList();
        }

        return new PagedResult<EmployeeSummaryDto>(rows, page, size, total);
    }

    public async Task<EmployeeDto> GetAsync(Guid id, CancellationToken ct)
    {
        var employee = await LoadAsync(id, ct);
        var dto = await ToDtoAsync(employee, ct);
        if (!dto.SensitiveMasked)
        {
            // Section 6: sensitive reads are logged as well as changes.
            audit.Record(db, "people.employee.sensitive_viewed", "employee", employee.Id, legalEntityId: employee.LegalEntityId);
            await db.SaveChangesAsync(ct);
        }

        return dto;
    }

    public async Task<EmployeeHistoryDto> HistoryAsync(Guid id, CancellationToken ct)
    {
        var employee = await LoadAsync(id, ct);
        var today = await TodayAsync(employee.LegalEntityId, ct);
        var episodes = await db.Episodes.AsNoTracking().Where(x => x.EmployeeId == id).OrderBy(x => x.Sequence).ToListAsync(ct);
        var versions = await db.Assignments.AsNoTracking().Where(x => x.EmployeeId == id).ToListAsync(ct);
        var names = await ManagerNamesAsync(versions, ct);
        return new EmployeeHistoryDto(id, today, episodes.Select(ep => new EpisodeHistoryDto(MapEpisode(ep),
            versions.Where(v => v.EpisodeId == ep.Id).OrderBy(v => v.EffectiveFrom).ThenBy(v => v.CreatedAt)
                .Select(v => MapAssignment(v, versions, ep, names)).ToList())).ToList());
    }

    public async Task<IReadOnlyList<WorkPermitAlertDto>> WorkPermitAlertsAsync(Guid? legalEntityId, CancellationToken ct)
    {
        var scope = await ReadScopeAsync(ct);
        var query = db.Employees.AsNoTracking().Where(e => e.WorkPermitExpiry != null);
        if (legalEntityId is { } le)
        {
            query = query.Where(e => e.LegalEntityId == le);
        }

        var candidates = (await query.ToListAsync(ct)).Where(e => scope.Allows(e.LegalEntityId)).ToList();
        var ids = candidates.Select(e => e.Id).ToList();
        var episodes = (await db.Episodes.AsNoTracking().Where(x => ids.Contains(x.EmployeeId)).ToListAsync(ct)).ToLookup(x => x.EmployeeId);
        var result = new List<WorkPermitAlertDto>();
        foreach (var e in candidates)
        {
            var today = await TodayAsync(e.LegalEntityId, ct);
            var stillEmployed = episodes[e.Id].Any(ep => ep.TerminationDate is null || ep.TerminationDate >= today);
            var alert = EmployeeRules.PermitAlert(e.WorkPermitExpiry, today);
            if (stillEmployed && alert is not null)
            {
                result.Add(new WorkPermitAlertDto(e.Id, e.EmployeeNumber, e.FullName, e.LegalEntityId, e.WorkPermitExpiry!.Value,
                    e.WorkPermitExpiry.Value.DayNumber - today.DayNumber, alert));
            }
        }

        return result.OrderBy(r => r.WorkPermitExpiry).ToList();
    }

    // ---------- Hire ----------

    public async Task<EmployeeDto> CreateAsync(CreateEmployeeRequest r, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.PeopleWrite, r.LegalEntityId, ct);
        var le = await org.GetLegalEntityAsync(r.LegalEntityId, ct);
        if (le is not { IsActive: true })
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["legalEntityId"] = ["Choose an active legal employer."] });
        }

        var today = await TodayAsync(le.Id, ct);
        var e = new FieldErrors();
        var number = EmployeeRules.NormalizeEmployeeNumber(r.EmployeeNumber, e);
        if (r.HireDate is null)
        {
            e.Add("hireDate", "Hire date is required.");
        }

        var hire = r.HireDate ?? today;
        var assignment = EmployeeRules.NormalizeAssignment(r.Assignment, hire, e);
        var personal = EmployeeRules.NormalizePersonal(r.Personal, hire, assignment?.WorkerCategory ?? r.Assignment?.WorkerCategory, e);
        var identifiers = EmployeeRules.NormalizeIdentifiers(r.Identifiers, personal?.Citizenship ?? r.Personal?.Citizenship, today, e);
        var contacts = EmployeeRules.NormalizeContacts(r.EmergencyContacts, e);
        var payment = EmployeeRules.NormalizePayment(r.PaymentDetails, e);
        if (assignment is not null)
        {
            await ValidateReferencesAsync(le.Id, null, hire, assignment, e, "assignment.", ct);
        }

        if (payment is not null)
        {
            await ValidateBankBranchAsync(payment, e, "paymentDetails.", ct);
        }

        if (number is not null && identifiers is not null)
        {
            await CheckDuplicatesAsync(le.Id, null, number, identifiers, e, ct);
        }

        e.ThrowIfAny();

        var employee = AddEmployee(le.Id, number!, hire, personal!, identifiers!, contacts!, assignment!, null, null);
        AddPaymentChange(employee, payment!, "hire", "Payment details given at hire", PaymentChangeStatuses.Pending, null);
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(employee, ct);
    }

    /// <summary>Adds the employee, the first episode, the hire version and contacts, with audit. Shared by hire and import.</summary>
    internal Employee AddEmployee(Guid legalEntityId, string number, DateOnly hire, Personal p, Identifiers i, IReadOnlyList<Contact> contacts,
        Assignment a, Guid? importBatchId, string? reason, Guid? id = null)
    {
        var employee = new Employee
        {
            Id = id ?? Guid.CreateVersion7(),
            LegalEntityId = legalEntityId,
            EmployeeNumber = number,
            CreatedByUserId = context.ActorUserId,
            ImportBatchId = importBatchId,
        };
        ApplyPersonal(employee, p);
        ApplyIdentifiers(employee, i);
        db.Employees.Add(employee);

        var episode = new EmploymentEpisode { LegalEntityId = legalEntityId, EmployeeId = employee.Id, Sequence = 1, HireDate = hire };
        db.Episodes.Add(episode);
        var version = NewVersion(employee, episode, hire, ChangeTypes.Hire, a, reason);
        db.Assignments.Add(version);
        AddContacts(employee, contacts);

        audit.Record(db, "people.employee.hired", "employee", employee.Id,
            after: AuditSnapshot.Of(new { employee = employee, episode, assignment = version, contacts = contacts.Count }),
            reason: reason, legalEntityId: legalEntityId);
        audit.Enqueue(db, EmployeeChangedEvent, new { employeeId = employee.Id, legalEntityId, change = "hired", effectiveFrom = hire });
        return employee;
    }

    // ---------- Personal data ----------

    public async Task<EmployeeDto> UpdateAsync(Guid id, UpdateEmployeeRequest r, CancellationToken ct)
    {
        var employee = await LoadAsync(id, ct);
        await access.RequireAsync(Permissions.PeopleWrite, employee.LegalEntityId, ct);
        if (r.Identifiers is not null && !await CanSeeSensitiveAsync(employee.LegalEntityId, ct))
        {
            throw new ForbiddenException("Changing restricted identifiers needs permission to view them.");
        }

        CheckVersion(employee, employee.Version, r.Version);
        var today = await TodayAsync(employee.LegalEntityId, ct);
        var episodes = await db.Episodes.AsNoTracking().Where(x => x.EmployeeId == id).ToListAsync(ct);
        var firstHire = episodes.Min(x => x.HireDate);
        var category = await CategoryOnAsync(id, episodes, firstHire, ct);

        var e = new FieldErrors();
        var personal = EmployeeRules.NormalizePersonal(r.Personal, firstHire, category, e);
        Identifiers? identifiers = null;
        if (r.Identifiers is not null)
        {
            identifiers = EmployeeRules.NormalizeIdentifiers(r.Identifiers, personal?.Citizenship ?? r.Personal?.Citizenship, today, e);
        }
        else if (personal?.Citizenship == Citizenships.NonKenyan && employee.Citizenship != Citizenships.NonKenyan)
        {
            e.Add("identifiers", "A non-citizen needs passport and work permit details; ask someone who can edit restricted identifiers.");
        }

        if (r.Reason is { Length: > 1000 })
        {
            e.Add("reason", "Must be at most 1000 characters.");
        }

        if (identifiers is not null)
        {
            await CheckDuplicatesAsync(employee.LegalEntityId, employee.Id, null, identifiers, e, ct);
        }

        e.ThrowIfAny();

        var before = AuditSnapshot.Of(employee);
        ApplyPersonal(employee, personal!);
        if (identifiers is not null)
        {
            ApplyIdentifiers(employee, identifiers);
        }

        audit.Record(db, "people.employee.updated", "employee", employee.Id, before, AuditSnapshot.Of(employee), r.Reason, employee.LegalEntityId);
        audit.Enqueue(db, EmployeeChangedEvent, new { employeeId = employee.Id, employee.LegalEntityId, change = "personal_updated" });
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(employee, ct);
    }

    public async Task<EmployeeDto> ReplaceContactsAsync(Guid id, ReplaceEmergencyContactsRequest r, CancellationToken ct)
    {
        var employee = await LoadAsync(id, ct);
        await access.RequireAsync(Permissions.PeopleWrite, employee.LegalEntityId, ct);
        var e = new FieldErrors();
        var contacts = EmployeeRules.NormalizeContacts(r.Contacts, e, "contacts");
        e.ThrowIfAny();

        var existing = await db.EmergencyContacts.Where(c => c.EmployeeId == id && c.IsActive).ToListAsync(ct);
        var before = AuditSnapshot.Of(existing.Select(c => new { c.Name, c.Relationship, c.Phone }));
        foreach (var c in existing)
        {
            c.IsActive = false;
        }

        AddContacts(employee, contacts!);
        audit.Record(db, "people.emergency_contacts.replaced", "employee", employee.Id, before,
            AuditSnapshot.Of(contacts!.Select(c => new { c.Name, c.Relationship, Phone = AuditSnapshot.Mask(c.Phone) })), r.Reason, employee.LegalEntityId);
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(employee, ct);
    }

    // ---------- Assignment history ----------

    public async Task<EmployeeHistoryDto> AddAssignmentAsync(Guid id, AddAssignmentRequest r, CancellationToken ct)
    {
        var employee = await LoadAsync(id, ct);
        await access.RequireAsync(Permissions.PeopleWrite, employee.LegalEntityId, ct);
        var today = await TodayAsync(employee.LegalEntityId, ct);
        var e = new FieldErrors();
        var changeType = EmployeeRules.Clean(r.ChangeType)?.ToLowerInvariant();
        if (changeType is null || !ChangeTypes.Manual.Contains(changeType))
        {
            e.Add("changeType", "Choose promotion, transfer, change, suspension or reinstatement.");
        }

        if (changeType == ChangeTypes.Suspension && string.IsNullOrWhiteSpace(r.Reason))
        {
            e.Add("reason", "Give the reason for the suspension.");
        }

        if (r.Reason is { Length: > 1000 })
        {
            e.Add("reason", "Must be at most 1000 characters.");
        }

        var assignment = EmployeeRules.NormalizeAssignment(r.Assignment, r.EffectiveFrom, e);
        e.ThrowIfAny();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockEmployeeAsync(employee.Id, ct);
        var episodes = await db.Episodes.Where(x => x.EmployeeId == id).ToListAsync(ct);
        var episode = episodes.FirstOrDefault(x => x.HireDate <= r.EffectiveFrom && (x.TerminationDate is null || x.TerminationDate >= r.EffectiveFrom));
        if (episode is null)
        {
            throw new BusinessRuleException("outside_employment", "The employee is not employed on that date.");
        }

        if (r.EffectiveFrom == episode.HireDate)
        {
            throw new BusinessRuleException("version_exists", "The hire assignment already starts on that date. Correct it instead.");
        }

        var versions = await db.Assignments.AsNoTracking().Where(x => x.EmployeeId == id).ToListAsync(ct);
        if (versions.Any(v => v.EpisodeId == episode.Id && !v.IsCancelled && v.EffectiveFrom == r.EffectiveFrom))
        {
            throw new BusinessRuleException("version_exists", "A change already starts on that date. Correct that change instead.");
        }

        var previous = History.AssignmentOn(versions, episode.Id, r.EffectiveFrom.AddDays(-1))!;
        if (changeType == ChangeTypes.Suspension && previous.Status == AssignmentStatuses.Suspended)
        {
            throw new BusinessRuleException("already_suspended", "The employee is already suspended on that date.");
        }

        if (changeType == ChangeTypes.Reinstatement && previous.Status != AssignmentStatuses.Suspended)
        {
            throw new BusinessRuleException("not_suspended", "Only a suspended employee can be reinstated.");
        }

        await ValidateReferencesAsync(employee.LegalEntityId, employee.Id, r.EffectiveFrom, assignment!, e, "assignment.", ct);
        e.ThrowIfAny();

        var status = changeType switch
        {
            ChangeTypes.Suspension => AssignmentStatuses.Suspended,
            ChangeTypes.Reinstatement => AssignmentStatuses.Active,
            _ => previous.Status,
        };
        var version = NewVersion(employee, episode, r.EffectiveFrom, changeType!, assignment!, EmployeeRules.Clean(r.Reason));
        version.Status = status;
        db.Assignments.Add(version);
        var backdated = r.EffectiveFrom < today;
        audit.Record(db, $"people.assignment.{changeType}", "assignment_version", version.Id, AuditSnapshot.Of(previous),
            AuditSnapshot.Of(new { version, backdated, insertedBeforeLaterChange = versions.Any(v => v.EpisodeId == episode.Id && !v.IsCancelled && v.EffectiveFrom > r.EffectiveFrom) }),
            EmployeeRules.Clean(r.Reason), employee.LegalEntityId);
        audit.Enqueue(db, EmployeeChangedEvent, new { employeeId = employee.Id, employee.LegalEntityId, change = changeType, effectiveFrom = r.EffectiveFrom, backdated });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await HistoryAsync(id, ct);
    }

    /// <summary>Cancels a version (kept, with the reason) and records its correction for the same date, atomically.</summary>
    public async Task<EmployeeHistoryDto> CorrectAssignmentAsync(Guid id, Guid versionId, CorrectAssignmentRequest r, CancellationToken ct)
    {
        var employee = await LoadAsync(id, ct);
        await access.RequireAsync(Permissions.PeopleWrite, employee.LegalEntityId, ct);
        var e = new FieldErrors();
        if (string.IsNullOrWhiteSpace(r.Reason))
        {
            e.Add("reason", "Explain what was wrong.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockEmployeeAsync(employee.Id, ct);
        var original = await db.Assignments.SingleOrDefaultAsync(v => v.Id == versionId && v.EmployeeId == id, ct)
            ?? throw new NotFoundException("Assignment version");
        if (original.IsCancelled)
        {
            throw new BusinessRuleException("already_cancelled", "This version was already cancelled.");
        }

        CheckVersion(original, original.Version, r.Version);
        var assignment = EmployeeRules.NormalizeAssignment(r.Assignment, original.EffectiveFrom, e);
        e.ThrowIfAny();
        await ValidateReferencesAsync(employee.LegalEntityId, employee.Id, original.EffectiveFrom, assignment!, e, "assignment.", ct);
        e.ThrowIfAny();

        var episode = await db.Episodes.AsNoTracking().SingleAsync(x => x.Id == original.EpisodeId, ct);
        var reason = EmployeeRules.Clean(r.Reason)!;
        var before = AuditSnapshot.Of(original);
        CancelVersion(original, reason);
        audit.Record(db, "people.assignment.cancelled", "assignment_version", original.Id, before, AuditSnapshot.Of(original), reason, employee.LegalEntityId);
        await db.SaveChangesAsync(ct);

        var replacement = NewVersion(employee, episode, original.EffectiveFrom, original.ChangeType, assignment!, reason);
        replacement.Status = original.Status;
        replacement.CorrectsVersionId = original.Id;
        db.Assignments.Add(replacement);
        audit.Record(db, "people.assignment.corrected", "assignment_version", replacement.Id, before, AuditSnapshot.Of(replacement), reason, employee.LegalEntityId);
        audit.Enqueue(db, EmployeeChangedEvent, new { employeeId = employee.Id, employee.LegalEntityId, change = "corrected", effectiveFrom = original.EffectiveFrom });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await HistoryAsync(id, ct);
    }

    /// <summary>Withdraws a change that has not taken effect yet. Changes already in effect are corrected, not cancelled.</summary>
    public async Task<EmployeeHistoryDto> CancelAssignmentAsync(Guid id, Guid versionId, CancelAssignmentRequest r, CancellationToken ct)
    {
        var employee = await LoadAsync(id, ct);
        await access.RequireAsync(Permissions.PeopleWrite, employee.LegalEntityId, ct);
        new Validator().Required("reason", r.Reason, 1000).ThrowIfInvalid();
        var today = await TodayAsync(employee.LegalEntityId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockEmployeeAsync(employee.Id, ct);
        var version = await db.Assignments.SingleOrDefaultAsync(v => v.Id == versionId && v.EmployeeId == id, ct)
            ?? throw new NotFoundException("Assignment version");
        CheckVersion(version, version.Version, r.Version);
        if (version.IsCancelled)
        {
            throw new BusinessRuleException("already_cancelled", "This version was already cancelled.");
        }

        if (version.ChangeType is ChangeTypes.Hire or ChangeTypes.Rehire)
        {
            throw new BusinessRuleException("cannot_cancel_start", "The first assignment of an employment cannot be cancelled. Correct it instead.");
        }

        if (version.EffectiveFrom <= today)
        {
            throw new BusinessRuleException("already_effective", "This change is already in effect. Record a new change or correct it instead.");
        }

        var before = AuditSnapshot.Of(version);
        CancelVersion(version, r.Reason!.Trim());
        audit.Record(db, "people.assignment.cancelled", "assignment_version", version.Id, before, AuditSnapshot.Of(version), r.Reason, employee.LegalEntityId);
        audit.Enqueue(db, EmployeeChangedEvent, new { employeeId = employee.Id, employee.LegalEntityId, change = "cancelled", effectiveFrom = version.EffectiveFrom });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await HistoryAsync(id, ct);
    }

    // ---------- Termination and rehire ----------

    public async Task<EmployeeHistoryDto> TerminateAsync(Guid id, TerminateRequest r, CancellationToken ct)
    {
        var employee = await LoadAsync(id, ct);
        await access.RequireAsync(Permissions.PeopleWrite, employee.LegalEntityId, ct);
        var type = EmployeeRules.Clean(r.TerminationType)?.ToLowerInvariant();
        new Validator()
            .Must(type is not null && TerminationTypes.All.Contains(type), "terminationType", $"Choose {string.Join(", ", TerminationTypes.All)}.")
            .Required("reason", r.Reason, 1000)
            .ThrowIfInvalid();
        var today = await TodayAsync(employee.LegalEntityId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockEmployeeAsync(employee.Id, ct);
        var episode = await db.Episodes.SingleOrDefaultAsync(x => x.EmployeeId == id && x.TerminationDate == null, ct)
            ?? throw new BusinessRuleException("not_employed", "The employee has no open employment to end.");
        CheckVersion(episode, episode.Version, r.Version);
        if (r.TerminationDate < episode.HireDate)
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["terminationDate"] = ["The last day cannot be before the hire date."] });
        }

        if (await db.Assignments.AnyAsync(v => v.EpisodeId == episode.Id && !v.IsCancelled && v.EffectiveFrom > r.TerminationDate, ct))
        {
            throw new BusinessRuleException("later_changes_exist", "Changes are planned after this last day. Cancel them first.");
        }

        var before = AuditSnapshot.Of(episode);
        episode.TerminationDate = r.TerminationDate;
        episode.TerminationType = type;
        episode.TerminationReason = r.Reason!.Trim();
        episode.TerminationRecordedAt = clock.GetUtcNow();
        episode.TerminationRecordedByUserId = context.ActorUserId;
        audit.Record(db, "people.employee.terminated", "employment_episode", episode.Id, before,
            AuditSnapshot.Of(new { episode, backdated = r.TerminationDate < today }), r.Reason, employee.LegalEntityId);
        audit.Enqueue(db, EmployeeChangedEvent, new { employeeId = employee.Id, employee.LegalEntityId, change = "terminated", effectiveFrom = r.TerminationDate });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await HistoryAsync(id, ct);
    }

    /// <summary>Withdraws a termination whose last day has not passed (e.g. a resignation withdrawn during notice).</summary>
    public async Task<EmployeeHistoryDto> RescindTerminationAsync(Guid id, RescindTerminationRequest r, CancellationToken ct)
    {
        var employee = await LoadAsync(id, ct);
        await access.RequireAsync(Permissions.PeopleWrite, employee.LegalEntityId, ct);
        new Validator().Required("reason", r.Reason, 1000).ThrowIfInvalid();
        var today = await TodayAsync(employee.LegalEntityId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockEmployeeAsync(employee.Id, ct);
        var episode = await db.Episodes.Where(x => x.EmployeeId == id).OrderByDescending(x => x.Sequence).FirstAsync(ct);
        CheckVersion(episode, episode.Version, r.Version);
        if (episode.TerminationDate is null)
        {
            throw new BusinessRuleException("not_terminated", "The employment has no termination to withdraw.");
        }

        if (episode.TerminationDate < today)
        {
            throw new BusinessRuleException("termination_in_effect", "The employment has already ended. Rehire the employee instead.");
        }

        var before = AuditSnapshot.Of(episode);
        episode.TerminationDate = null;
        episode.TerminationType = null;
        episode.TerminationReason = null;
        episode.TerminationRecordedAt = null;
        episode.TerminationRecordedByUserId = null;
        audit.Record(db, "people.employee.termination_rescinded", "employment_episode", episode.Id, before, AuditSnapshot.Of(episode), r.Reason, employee.LegalEntityId);
        audit.Enqueue(db, EmployeeChangedEvent, new { employeeId = employee.Id, employee.LegalEntityId, change = "termination_rescinded" });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await HistoryAsync(id, ct);
    }

    public async Task<EmployeeHistoryDto> RehireAsync(Guid id, RehireRequest r, CancellationToken ct)
    {
        var employee = await LoadAsync(id, ct);
        await access.RequireAsync(Permissions.PeopleWrite, employee.LegalEntityId, ct);
        var e = new FieldErrors();
        var assignment = EmployeeRules.NormalizeAssignment(r.Assignment, r.HireDate, e);
        if (r.Reason is { Length: > 1000 })
        {
            e.Add("reason", "Must be at most 1000 characters.");
        }

        if (assignment is not null && EmployeeRules.AgeOn(employee.DateOfBirth, r.HireDate) < EmployeeRules.AdultAge
            && !(assignment.WorkerCategory == WorkerCategories.Intern && employee.InternGuardianConsent))
        {
            e.Add("hireDate", "Employees must be 18 or older, unless hired as an intern/attaché with guardian consent.");
        }

        e.ThrowIfAny();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockEmployeeAsync(employee.Id, ct);
        var last = await db.Episodes.Where(x => x.EmployeeId == id).OrderByDescending(x => x.Sequence).FirstAsync(ct);
        if (last.TerminationDate is null || r.HireDate <= last.TerminationDate)
        {
            throw new BusinessRuleException("still_employed", "A rehire must start after the previous employment ended.");
        }

        await ValidateReferencesAsync(employee.LegalEntityId, employee.Id, r.HireDate, assignment!, e, "assignment.", ct);
        e.ThrowIfAny();

        var episode = new EmploymentEpisode { LegalEntityId = employee.LegalEntityId, EmployeeId = employee.Id, Sequence = last.Sequence + 1, HireDate = r.HireDate };
        db.Episodes.Add(episode);
        var version = NewVersion(employee, episode, r.HireDate, ChangeTypes.Rehire, assignment!, EmployeeRules.Clean(r.Reason));
        db.Assignments.Add(version);
        audit.Record(db, "people.employee.rehired", "employment_episode", episode.Id, AuditSnapshot.Of(last),
            AuditSnapshot.Of(new { episode, assignment = version }), r.Reason, employee.LegalEntityId);
        audit.Enqueue(db, EmployeeChangedEvent, new { employeeId = employee.Id, employee.LegalEntityId, change = "rehired", effectiveFrom = r.HireDate });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await HistoryAsync(id, ct);
    }

    // ---------- Payment details (four-eyes) ----------

    public async Task<PaymentChangeDto> RequestPaymentChangeAsync(Guid id, RequestPaymentChangeRequest r, CancellationToken ct)
    {
        var employee = await LoadAsync(id, ct);
        await access.RequireAsync(Permissions.PeopleWrite, employee.LegalEntityId, ct);
        var e = new FieldErrors();
        var details = EmployeeRules.NormalizePayment(r.Details, e, "details.");
        if (string.IsNullOrWhiteSpace(r.Reason))
        {
            e.Add("reason", "Give the reason for the change.");
        }

        if (details is not null)
        {
            await ValidateBankBranchAsync(details, e, "details.", ct);
        }

        e.ThrowIfAny();
        if (await db.PaymentChanges.AnyAsync(c => c.EmployeeId == id && c.Status == PaymentChangeStatuses.Pending, ct))
        {
            throw new ConflictException("pending_change_exists", "A payment change is already waiting for approval. Approve, reject or withdraw it first.");
        }

        var change = AddPaymentChange(employee, details!, "change", r.Reason!.Trim(), PaymentChangeStatuses.Pending, null);
        await db.SaveChangesAsync(ct);
        return await MapChangeAsync(change, employee, ct);
    }

    public async Task<PagedResult<PaymentChangeDto>> ListPaymentChangesAsync(string? status, Guid? legalEntityId, int? page, int? pageSize, CancellationToken ct)
    {
        var scope = await ReadScopeAsync(ct);
        var (p, s) = Paging.Normalize(page, pageSize);
        var query = db.PaymentChanges.AsNoTracking().Where(c => c.Status == (status ?? PaymentChangeStatuses.Pending));
        if (!scope.AllLegalEntities)
        {
            var ids = scope.LegalEntityIds.ToList();
            query = query.Where(c => ids.Contains(c.LegalEntityId));
        }

        if (legalEntityId is { } le)
        {
            query = query.Where(c => c.LegalEntityId == le);
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(c => c.RequestedAt).Skip((p - 1) * s).Take(s).ToListAsync(ct);
        var employeeIds = rows.Select(r => r.EmployeeId).Distinct().ToList();
        var employees = await db.Employees.AsNoTracking().Where(x => employeeIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var result = new List<PaymentChangeDto>();
        foreach (var c in rows)
        {
            result.Add(await MapChangeAsync(c, employees[c.EmployeeId], ct));
        }

        return new PagedResult<PaymentChangeDto>(result, p, s, total);
    }

    /// <summary>Applies proposed payment details. The approver must be a different user from the requester.</summary>
    public async Task<PaymentChangeDto> ApprovePaymentChangeAsync(Guid changeId, DecidePaymentChangeRequest r, CancellationToken ct)
    {
        var (change, employee) = await LoadChangeAsync(changeId, r.Version, ct);
        if (change.RequestedByUserId == context.ActorUserId)
        {
            throw new BusinessRuleException("self_approval", "Someone other than the requester must approve payment details.");
        }

        var e = new FieldErrors();
        await ValidateBankBranchAsync(new PaymentDetails(change.PaymentMethod, change.BankCode, change.BankBranchCode, change.AccountNo, change.AccountName, change.MpesaPhone), e, "details.", ct);
        e.ThrowIfAny();

        var before = AuditSnapshot.Of(employee);
        employee.PaymentMethod = change.PaymentMethod;
        employee.BankCode = change.BankCode;
        employee.BankBranchCode = change.BankBranchCode;
        employee.AccountNo = change.AccountNo;
        employee.AccountName = change.AccountName;
        employee.MpesaPhone = change.MpesaPhone;
        change.Status = PaymentChangeStatuses.Approved;
        change.DecidedByUserId = context.ActorUserId;
        change.DecidedAt = clock.GetUtcNow();
        change.DecisionNote = EmployeeRules.Clean(r.Note);
        audit.Record(db, "people.payment_details.approved", "payment_detail_change", change.Id, before, AuditSnapshot.Of(employee), change.DecisionNote, employee.LegalEntityId);
        audit.Enqueue(db, EmployeeChangedEvent, new { employeeId = employee.Id, employee.LegalEntityId, change = "payment_details_changed" });
        await db.SaveChangesAsync(ct);
        return await MapChangeAsync(change, employee, ct);
    }

    /// <summary>Rejects a proposal; the requester withdrawing their own proposal is recorded as withdrawn.</summary>
    public async Task<PaymentChangeDto> RejectPaymentChangeAsync(Guid changeId, DecidePaymentChangeRequest r, CancellationToken ct)
    {
        var (change, employee) = await LoadChangeAsync(changeId, r.Version, ct);
        var own = change.RequestedByUserId == context.ActorUserId;
        if (!own)
        {
            new Validator().Required("note", r.Note, 1000).ThrowIfInvalid();
        }

        var before = AuditSnapshot.Of(change);
        change.Status = own ? PaymentChangeStatuses.Withdrawn : PaymentChangeStatuses.Rejected;
        change.DecidedByUserId = context.ActorUserId;
        change.DecidedAt = clock.GetUtcNow();
        change.DecisionNote = EmployeeRules.Clean(r.Note);
        audit.Record(db, $"people.payment_details.{change.Status}", "payment_detail_change", change.Id, before, AuditSnapshot.Of(change), change.DecisionNote, employee.LegalEntityId);
        await db.SaveChangesAsync(ct);
        return await MapChangeAsync(change, employee, ct);
    }

    internal PaymentDetailsChange AddPaymentChange(Employee employee, PaymentDetails d, string source, string? reason, string status, Guid? decidedBy)
    {
        var change = new PaymentDetailsChange
        {
            LegalEntityId = employee.LegalEntityId,
            EmployeeId = employee.Id,
            PaymentMethod = d.PaymentMethod,
            BankCode = d.BankCode,
            BankBranchCode = d.BankBranchCode,
            AccountNo = d.AccountNo,
            AccountName = d.AccountName,
            MpesaPhone = d.MpesaPhone,
            Source = source,
            Reason = reason,
            Status = status,
            RequestedByUserId = context.ActorUserId,
            RequestedAt = clock.GetUtcNow(),
        };
        db.PaymentChanges.Add(change);
        audit.Record(db, "people.payment_details.requested", "payment_detail_change", change.Id, after: AuditSnapshot.Of(change), reason: reason, legalEntityId: employee.LegalEntityId);
        return change;
    }

    // ---------- Validation helpers ----------

    /// <summary>Organization references must be active records of the same employer; the manager must be employed then and not create a loop.</summary>
    internal async Task ValidateReferencesAsync(Guid legalEntityId, Guid? employeeId, DateOnly effectiveFrom, Assignment a, FieldErrors e, string prefix, CancellationToken ct, OrgReferenceData? refs = null)
    {
        refs ??= await org.GetReferenceDataAsync(legalEntityId, ct);
        void Check(string kind, Guid? value, string field)
        {
            if (value is { } v && refs.ById(kind, v) is not { IsActive: true })
            {
                e.Add(prefix + field, "Choose an active record of the same legal employer.");
            }
        }

        Check(OrgReferenceData.Departments, a.DepartmentId, "departmentId");
        Check(OrgReferenceData.Positions, a.PositionId, "positionId");
        Check(OrgReferenceData.Branches, a.BranchId, "branchId");
        Check(OrgReferenceData.CostCenters, a.CostCenterId, "costCenterId");
        Check(OrgReferenceData.Grades, a.GradeId, "gradeId");
        Check(OrgReferenceData.WorkSchedules, a.WorkScheduleId, "workScheduleId");

        if (a.ManagerEmployeeId is not { } managerId)
        {
            return;
        }

        if (managerId == employeeId)
        {
            e.Add(prefix + "managerEmployeeId", "An employee cannot be their own manager.");
            return;
        }

        var managers = await ManagerMapAsync(legalEntityId, effectiveFrom, ct);
        if (!managers.ContainsKey(managerId))
        {
            e.Add(prefix + "managerEmployeeId", "The manager must be an employee of the same employer on that date.");
        }
        else if (employeeId is { } self && History.WouldCreateManagerCycle(self, managerId, managers))
        {
            e.Add(prefix + "managerEmployeeId", "This manager would create a circular reporting line.");
        }
    }

    /// <summary>Who manages whom on a date, for employees employed on that date.</summary>
    internal async Task<Dictionary<Guid, Guid?>> ManagerMapAsync(Guid legalEntityId, DateOnly date, CancellationToken ct)
    {
        var episodes = await db.Episodes.AsNoTracking()
            .Where(x => x.LegalEntityId == legalEntityId && x.HireDate <= date && (x.TerminationDate == null || x.TerminationDate >= date))
            .ToListAsync(ct);
        var episodeIds = episodes.Select(x => x.Id).ToList();
        var versions = await db.Assignments.AsNoTracking()
            .Where(v => episodeIds.Contains(v.EpisodeId) && !v.IsCancelled && v.EffectiveFrom <= date)
            .ToListAsync(ct);
        return episodes.ToDictionary(ep => ep.EmployeeId, ep => History.AssignmentOn(versions, ep.Id, date)?.ManagerEmployeeId);
    }

    internal async Task ValidateBankBranchAsync(PaymentDetails d, FieldErrors e, string prefix, CancellationToken ct)
    {
        if (d.PaymentMethod != PaymentMethods.Bank)
        {
            return;
        }

        var ok = await db.BankBranches.AsNoTracking().AnyAsync(b => b.BankCode == d.BankCode && b.BranchCode == d.BankBranchCode && b.IsActive, ct);
        if (!ok)
        {
            e.Add(prefix + "bankBranchCode", "Choose a bank and branch from the bank reference table.");
        }
    }

    /// <summary>Employee number and statutory identifiers are unique within the employer (EMP-03).</summary>
    internal async Task CheckDuplicatesAsync(Guid legalEntityId, Guid? excludeId, string? number, Identifiers i, FieldErrors e, CancellationToken ct)
    {
        var matches = await db.Employees.AsNoTracking()
            .Where(x => x.LegalEntityId == legalEntityId && x.Id != excludeId
                        && ((number != null && x.EmployeeNumber == number)
                            || (i.NationalId != null && x.NationalId == i.NationalId)
                            || (i.PassportNo != null && x.PassportNo == i.PassportNo)
                            || (i.KraPin != null && x.KraPin == i.KraPin)
                            || (i.NssfNo != null && x.NssfNo == i.NssfNo)
                            || (i.ShaNo != null && x.ShaNo == i.ShaNo)))
            .Select(x => new { x.EmployeeNumber, x.NationalId, x.PassportNo, x.KraPin, x.NssfNo, x.ShaNo })
            .ToListAsync(ct);
        foreach (var m in matches)
        {
            if (number is not null && m.EmployeeNumber == number) e.Add("employeeNumber", "This employee number is already used by this employer.");
            if (i.NationalId is not null && m.NationalId == i.NationalId) e.Add("identifiers.nationalId", $"Already recorded for employee {m.EmployeeNumber}.");
            if (i.PassportNo is not null && m.PassportNo == i.PassportNo) e.Add("identifiers.passportNo", $"Already recorded for employee {m.EmployeeNumber}.");
            if (i.KraPin is not null && m.KraPin == i.KraPin) e.Add("identifiers.kraPin", $"Already recorded for employee {m.EmployeeNumber}.");
            if (i.NssfNo is not null && m.NssfNo == i.NssfNo) e.Add("identifiers.nssfNo", $"Already recorded for employee {m.EmployeeNumber}.");
            if (i.ShaNo is not null && m.ShaNo == i.ShaNo) e.Add("identifiers.shaNo", $"Already recorded for employee {m.EmployeeNumber}.");
        }
    }

    // ---------- Shared helpers ----------

    internal async Task<DateOnly> TodayAsync(Guid legalEntityId, CancellationToken ct)
    {
        if (_today.TryGetValue(legalEntityId, out var cached))
        {
            return cached;
        }

        var le = await org.GetLegalEntityAsync(legalEntityId, ct);
        var tz = TimeZoneInfo.FindSystemTimeZoneById(le?.Timezone ?? "Africa/Nairobi");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), tz).DateTime);
        _today[legalEntityId] = today;
        return today;
    }

    private async Task<PermissionScope> ReadScopeAsync(CancellationToken ct)
    {
        var scope = await access.ScopeAsync(Permissions.PeopleRead, ct);
        if (!scope.Granted)
        {
            throw new ForbiddenException();
        }

        return scope;
    }

    private async Task<bool> CanSeeSensitiveAsync(Guid legalEntityId, CancellationToken ct)
        => (await access.ScopeAsync(Permissions.PeopleSensitiveRead, ct)).Allows(legalEntityId);

    private async Task<Employee> LoadAsync(Guid id, CancellationToken ct)
    {
        var scope = await ReadScopeAsync(ct);
        var employee = await db.Employees.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (employee is null || !scope.Allows(employee.LegalEntityId))
        {
            throw new NotFoundException("Employee");
        }

        return employee;
    }

    private async Task<(PaymentDetailsChange Change, Employee Employee)> LoadChangeAsync(Guid changeId, uint version, CancellationToken ct)
    {
        var scope = await ReadScopeAsync(ct);
        var change = await db.PaymentChanges.SingleOrDefaultAsync(c => c.Id == changeId, ct);
        if (change is null || !scope.Allows(change.LegalEntityId))
        {
            throw new NotFoundException("Payment change");
        }

        await access.RequireAsync(Permissions.PeopleWrite, change.LegalEntityId, ct);
        CheckVersion(change, change.Version, version);
        if (change.Status != PaymentChangeStatuses.Pending)
        {
            throw new BusinessRuleException("not_pending", "This payment change was already decided.");
        }

        var employee = await db.Employees.SingleAsync(x => x.Id == change.EmployeeId, ct);
        return (change, employee);
    }

    /// <summary>Serializes history changes of one employee (two concurrent edits cannot both pass the checks).</summary>
    private async Task LockEmployeeAsync(Guid employeeId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Employee history changes must run in a transaction.");
        }

        await db.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtextextended({"people.employee:" + employeeId}, 0))", ct);
    }

    private async Task<string?> CategoryOnAsync(Guid employeeId, List<EmploymentEpisode> episodes, DateOnly date, CancellationToken ct)
    {
        var versions = await db.Assignments.AsNoTracking().Where(v => v.EmployeeId == employeeId && !v.IsCancelled).ToListAsync(ct);
        var ep = History.EpisodeOn(episodes, date) ?? episodes.OrderBy(x => x.Sequence).First();
        return History.AssignmentOn(versions, ep.Id, date < ep.HireDate ? ep.HireDate : date)?.WorkerCategory;
    }

    private AssignmentVersion NewVersion(Employee employee, EmploymentEpisode episode, DateOnly from, string changeType, Assignment a, string? reason)
        => new()
        {
            LegalEntityId = employee.LegalEntityId,
            EmployeeId = employee.Id,
            EpisodeId = episode.Id,
            EffectiveFrom = from,
            ChangeType = changeType,
            Status = AssignmentStatuses.Active,
            WorkerCategory = a.WorkerCategory,
            PayBasis = a.PayBasis,
            JobTitle = a.JobTitle,
            ContractEndDate = a.ContractEndDate,
            ProbationEndDate = a.ProbationEndDate,
            DepartmentId = a.DepartmentId,
            PositionId = a.PositionId,
            BranchId = a.BranchId,
            CostCenterId = a.CostCenterId,
            GradeId = a.GradeId,
            WorkScheduleId = a.WorkScheduleId,
            ManagerEmployeeId = a.ManagerEmployeeId,
            Reason = reason,
            CreatedAt = clock.GetUtcNow(),
            CreatedByUserId = context.ActorUserId,
        };

    private void CancelVersion(AssignmentVersion version, string reason)
    {
        version.IsCancelled = true;
        version.CancelledAt = clock.GetUtcNow();
        version.CancelledByUserId = context.ActorUserId;
        version.CancelReason = reason;
    }

    private void AddContacts(Employee employee, IReadOnlyList<Contact> contacts)
    {
        for (var n = 0; n < contacts.Count; n++)
        {
            db.EmergencyContacts.Add(new EmergencyContact
            {
                LegalEntityId = employee.LegalEntityId,
                EmployeeId = employee.Id,
                Name = contacts[n].Name,
                Relationship = contacts[n].Relationship,
                Phone = contacts[n].Phone,
                SortOrder = n,
            });
        }
    }

    private static void ApplyPersonal(Employee e, Personal p)
    {
        e.FirstName = p.FirstName;
        e.MiddleName = p.MiddleName;
        e.LastName = p.LastName;
        e.DateOfBirth = p.DateOfBirth;
        e.Gender = p.Gender;
        e.Citizenship = p.Citizenship;
        e.ResidencyStatus = p.ResidencyStatus;
        e.PhoneMobile = p.PhoneMobile;
        e.EmailWork = p.EmailWork;
        e.EmailPersonal = p.EmailPersonal;
        e.InternGuardianConsent = p.InternGuardianConsent;
    }

    private static void ApplyIdentifiers(Employee e, Identifiers i)
    {
        e.NationalId = i.NationalId;
        e.PassportNo = i.PassportNo;
        e.WorkPermitNo = i.WorkPermitNo;
        e.WorkPermitExpiry = i.WorkPermitExpiry;
        e.KraPin = i.KraPin;
        e.NssfNo = i.NssfNo;
        e.ShaNo = i.ShaNo;
        e.HelbRef = i.HelbRef;
        e.DisabilityCertNo = i.DisabilityCertNo;
        e.DisabilityCertExpiry = i.DisabilityCertExpiry;
    }

    private void CheckVersion(object entity, uint current, uint expected)
    {
        if (current != expected)
        {
            throw new ConflictException("concurrency_conflict", "The record was changed by someone else. Reload and try again.");
        }

        db.Entry(entity).Property(nameof(YeaKazi.BuildingBlocks.Domain.IVersioned.Version)).OriginalValue = expected;
    }

    private async Task<Dictionary<Guid, string>> ManagerNamesAsync(IEnumerable<AssignmentVersion> versions, CancellationToken ct)
    {
        var ids = versions.Where(v => v.ManagerEmployeeId != null).Select(v => v.ManagerEmployeeId!.Value).Distinct().ToList();
        return ids.Count == 0
            ? new Dictionary<Guid, string>()
            : (await db.Employees.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct)).ToDictionary(x => x.Id, x => $"{x.FullName} ({x.EmployeeNumber})");
    }

    private async Task<EmployeeDto> ToDtoAsync(Employee x, CancellationToken ct)
    {
        var today = await TodayAsync(x.LegalEntityId, ct);
        var sensitive = await CanSeeSensitiveAsync(x.LegalEntityId, ct);
        var episodes = await db.Episodes.AsNoTracking().Where(v => v.EmployeeId == x.Id).ToListAsync(ct);
        var versions = await db.Assignments.AsNoTracking().Where(v => v.EmployeeId == x.Id).ToListAsync(ct);
        var contacts = await db.EmergencyContacts.AsNoTracking().Where(c => c.EmployeeId == x.Id && c.IsActive).OrderBy(c => c.SortOrder).ToListAsync(ct);
        var pending = await db.PaymentChanges.AsNoTracking().SingleOrDefaultAsync(c => c.EmployeeId == x.Id && c.Status == PaymentChangeStatuses.Pending, ct);
        var episode = History.RelevantEpisode(episodes, today);
        var current = episode is null ? null : History.AssignmentOn(versions, episode.Id, today < episode.HireDate ? episode.HireDate : today);
        var names = await ManagerNamesAsync(current is null ? [] : [current], ct);
        string? M(string? v) => sensitive ? v : AuditSnapshot.Mask(v);

        return new EmployeeDto(
            x.Id, x.LegalEntityId, x.EmployeeNumber, History.StatusOn(episodes, versions, today),
            new PersonalDto(x.FirstName, x.MiddleName, x.LastName, x.FullName, x.DateOfBirth, x.Gender, x.Citizenship, x.ResidencyStatus,
                x.PhoneMobile, x.EmailWork, x.EmailPersonal, x.InternGuardianConsent),
            new IdentifiersDto(M(x.NationalId), M(x.PassportNo), M(x.WorkPermitNo), x.WorkPermitExpiry, M(x.KraPin), M(x.NssfNo), M(x.ShaNo),
                M(x.HelbRef), M(x.DisabilityCertNo), x.DisabilityCertExpiry),
            !sensitive,
            contacts.Select(c => new EmergencyContactDto(c.Id, c.Name, c.Relationship, c.Phone)).ToList(),
            x.PaymentMethod is null ? null : new PaymentDetailsDto(x.PaymentMethod, x.BankCode, x.BankBranchCode, M(x.AccountNo), x.AccountName, M(x.MpesaPhone)),
            pending is null ? null : await MapChangeAsync(pending, x, ct),
            episode is null ? null : MapEpisode(episode),
            current is null || episode is null ? null : MapAssignment(current, versions, episode, names),
            EmployeeRules.Gaps(x, current?.WorkerCategory, today),
            EmployeeRules.PermitAlert(x.WorkPermitExpiry, today),
            x.ImportBatchId, x.Version, x.CreatedAt);
    }

    private async Task<PaymentChangeDto> MapChangeAsync(PaymentDetailsChange c, Employee employee, CancellationToken ct)
    {
        var sensitive = await CanSeeSensitiveAsync(c.LegalEntityId, ct);
        string? M(string? v) => sensitive ? v : AuditSnapshot.Mask(v);
        var warnings = new List<string>();
        if (c.Status == PaymentChangeStatuses.Pending)
        {
            if (c.AccountNo is not null && await db.Employees.AsNoTracking().AnyAsync(x => x.LegalEntityId == c.LegalEntityId && x.Id != c.EmployeeId
                    && x.BankCode == c.BankCode && x.AccountNo == c.AccountNo, ct))
            {
                warnings.Add("Another employee of this employer is paid into the same bank account.");
            }

            if (c.MpesaPhone is not null && await db.Employees.AsNoTracking().AnyAsync(x => x.LegalEntityId == c.LegalEntityId && x.Id != c.EmployeeId
                    && x.MpesaPhone == c.MpesaPhone, ct))
            {
                warnings.Add("Another employee of this employer is paid to the same M-Pesa number.");
            }

            if (c.PaymentMethod == PaymentMethods.Cash)
            {
                warnings.Add("Cash payment: approve only where bank or M-Pesa is not possible.");
            }

            if (c.AccountName is not null && !NameMatches(c.AccountName, employee))
            {
                warnings.Add("The account name does not contain the employee's first or last name.");
            }
        }

        return new PaymentChangeDto(c.Id, c.EmployeeId, employee.EmployeeNumber, employee.FullName, c.LegalEntityId, c.PaymentMethod, c.BankCode,
            c.BankBranchCode, M(c.AccountNo), c.AccountName, M(c.MpesaPhone), c.Source, c.Status, c.Reason, c.RequestedByUserId, c.RequestedAt,
            c.DecidedByUserId, c.DecidedAt, c.DecisionNote, warnings, c.Version);
    }

    private static bool NameMatches(string accountName, Employee e)
    {
        var upper = accountName.ToUpperInvariant();
        return upper.Contains(e.FirstName.ToUpperInvariant()) || upper.Contains(e.LastName.ToUpperInvariant());
    }

    private static EpisodeDto MapEpisode(EmploymentEpisode x)
        => new(x.Id, x.Sequence, x.HireDate, x.TerminationDate, x.TerminationType, x.TerminationReason, x.Version);

    private static AssignmentDto MapAssignment(AssignmentVersion v, IReadOnlyCollection<AssignmentVersion> all, EmploymentEpisode episode, IReadOnlyDictionary<Guid, string> names)
        => new(v.Id, v.EpisodeId, v.EffectiveFrom, History.EffectiveTo(all, v, episode), v.ChangeType, v.Status, v.WorkerCategory, v.PayBasis,
            v.JobTitle, v.ContractEndDate, v.ProbationEndDate, v.DepartmentId, v.PositionId, v.BranchId, v.CostCenterId, v.GradeId, v.WorkScheduleId,
            v.ManagerEmployeeId, v.ManagerEmployeeId is { } m && names.TryGetValue(m, out var n) ? n : null, v.Reason, v.CorrectsVersionId,
            v.IsCancelled, v.CancelReason, v.CancelledAt, v.CreatedAt, v.Version);
}
