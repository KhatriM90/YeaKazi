using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using YeaKazi.BuildingBlocks.Audit;
using YeaKazi.BuildingBlocks.Contracts;
using YeaKazi.BuildingBlocks.Errors;
using YeaKazi.BuildingBlocks.Http;
using YeaKazi.BuildingBlocks.Security;
using YeaKazi.BuildingBlocks.Tenancy;
using YeaKazi.BuildingBlocks.Validation;
using YeaKazi.People.Domain;
using YeaKazi.People.Persistence;

namespace YeaKazi.People.Services;

public sealed record ImportColumnDto(string Name, bool Required, string Description);

public sealed record UploadImportRequest(Guid LegalEntityId, string? FileName, string? Content);

public sealed record ImportBatchDto(
    Guid Id, Guid LegalEntityId, string FileName, string Status, int TotalRows, int ErrorRows, int ValidRows, Guid? UploadedByUserId,
    string? UploadedByDisplay, DateTimeOffset UploadedAt, Guid? CommittedByUserId, DateTimeOffset? CommittedAt, bool CanCommit,
    string? CommitBlockedReason, uint Version);

public sealed record ImportRowDto(int RowNumber, string? EmployeeNumber, string? Name, IReadOnlyList<FieldError> Errors, Guid? EmployeeId);

/// <summary>
/// MIG-01 (employees only): upload a CSV, see every row's errors before anything is written,
/// then a second user commits the whole batch in one transaction. A batch with any error cannot be
/// committed; the user fixes the file and uploads it again. Commit re-validates against current data.
/// </summary>
public sealed partial class ImportService(
    PeopleDbContext db,
    EmployeeService employees,
    IAccessControl access,
    AuditTrail audit,
    IOrganizationReferences org,
    RequestContext context,
    TimeProvider clock)
{
    public const int MaxRows = 2000;
    public const int MaxChars = 4 * 1024 * 1024;

    public static readonly IReadOnlyList<ImportColumnDto> Columns =
    [
        new("employee_number", true, "Unique within the employer; up to 20 letters, digits, - / _"),
        new("hire_date", true, "YYYY-MM-DD or DD/MM/YYYY"),
        new("first_name", true, "Up to 80 characters"),
        new("middle_name", false, "Up to 80 characters"),
        new("last_name", true, "Up to 80 characters"),
        new("date_of_birth", true, "Age 18+ at hire unless intern with guardian consent"),
        new("gender", true, "female, male or intersex (statutory leave eligibility only)"),
        new("citizenship", true, "kenyan or non_kenyan"),
        new("residency_status", true, "resident or non_resident"),
        new("national_id", false, "6-9 digits; national_id or passport_no is required"),
        new("passport_no", false, "Letters and digits; required for non-citizens"),
        new("work_permit_no", false, "Required for non-citizens"),
        new("work_permit_expiry", false, "Required for non-citizens"),
        new("kra_pin", true, "Individual PIN, e.g. A123456789Z"),
        new("nssf_no", false, "Digits; needed before payroll for non-contractors"),
        new("sha_no", false, "SHA/SHIF member number; needed before payroll for non-contractors"),
        new("helb_ref", false, "HELB reference where applicable"),
        new("disability_cert_no", false, "With disability_cert_expiry when an exemption is claimed"),
        new("disability_cert_expiry", false, "Certificate expiry date"),
        new("phone_mobile", true, "+2547XXXXXXXX, +2541XXXXXXXX or 07/01 local form"),
        new("email_work", false, "Work or personal email is required"),
        new("email_personal", false, "Work or personal email is required"),
        new("intern_guardian_consent", false, "yes/no; only for interns under 18"),
        new("worker_category", true, "permanent, fixed_term, probation, casual, piece_rate, intern or contractor"),
        new("pay_basis", true, "monthly, daily, hourly or piece_rate (must suit the category)"),
        new("job_title", false, "Up to 120 characters"),
        new("contract_end_date", false, "Required for fixed_term"),
        new("probation_end_date", false, "Required for probation"),
        new("department_code", false, "Code from organization setup"),
        new("position_code", false, "Code from organization setup"),
        new("branch_code", false, "Code from organization setup (location)"),
        new("cost_center_code", false, "Code from organization setup"),
        new("grade_code", false, "Code from organization setup"),
        new("work_schedule_code", false, "Code from organization setup"),
        new("manager_employee_number", false, "Existing employee or another row of this file"),
        new("payment_method", true, "bank, mpesa or cash"),
        new("bank_code", false, "From the bank reference table (bank payments)"),
        new("bank_branch_code", false, "From the bank reference table (bank payments)"),
        new("account_no", false, "Bank payments"),
        new("account_name", false, "Bank payments"),
        new("mpesa_phone", false, "M-Pesa registered number (M-Pesa payments)"),
        new("emergency_contact_name", true, "At least one emergency contact"),
        new("emergency_contact_relationship", true, string.Empty),
        new("emergency_contact_phone", true, "International format, e.g. +254712345678"),
        new("emergency_contact2_name", false, "Optional second contact"),
        new("emergency_contact2_relationship", false, string.Empty),
        new("emergency_contact2_phone", false, string.Empty),
    ];

    private static readonly HashSet<string> ColumnNames = Columns.Select(c => c.Name).ToHashSet();

    public static string TemplateCsv() => Csv.Write([Columns.Select(c => c.Name)]);

    public async Task<ImportBatchDto> UploadAsync(UploadImportRequest r, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.PeopleWrite, r.LegalEntityId, ct);
        var le = await org.GetLegalEntityAsync(r.LegalEntityId, ct);
        var fileName = EmployeeRules.Clean(r.FileName) ?? "employees.csv";
        new Validator()
            .Must(le is { IsActive: true }, "legalEntityId", "Choose an active legal employer.")
            .Required("content", r.Content, MaxChars)
            .Optional("fileName", fileName, 200)
            .ThrowIfInvalid();

        List<List<string>> table;
        try
        {
            table = Csv.Parse(r.Content!);
        }
        catch (FormatException ex)
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["content"] = [ex.Message] });
        }

        if (table.Count < 2)
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["content"] = ["The file needs a header row and at least one employee."] });
        }

        var header = table[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        var unknown = header.Where(h => !ColumnNames.Contains(h)).ToList();
        var missing = Columns.Where(c => c.Required && !header.Contains(c.Name)).Select(c => c.Name).ToList();
        var duplicated = header.GroupBy(h => h).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (unknown.Count > 0 || missing.Count > 0 || duplicated.Count > 0)
        {
            var errors = new Dictionary<string, string[]>();
            if (unknown.Count > 0) errors["header.unknown"] = [$"Unknown columns: {string.Join(", ", unknown)}. Download the template."];
            if (missing.Count > 0) errors["header.missing"] = [$"Missing columns: {string.Join(", ", missing)}."];
            if (duplicated.Count > 0) errors["header.duplicated"] = [$"Repeated columns: {string.Join(", ", duplicated)}."];
            throw new ValidationException(errors);
        }

        if (table.Count - 1 > MaxRows)
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["content"] = [$"A file may hold at most {MaxRows} employees."] });
        }

        var batch = new ImportBatch
        {
            LegalEntityId = le!.Id,
            FileName = fileName,
            FileSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(r.Content!))).ToLowerInvariant(),
            UploadedByUserId = context.ActorUserId,
            UploadedByDisplay = context.ActorDisplay,
            UploadedAt = clock.GetUtcNow(),
        };
        var rows = new List<ImportRow>();
        for (var n = 1; n < table.Count; n++)
        {
            var cells = table[n];
            var data = new Dictionary<string, string>();
            for (var c = 0; c < header.Count; c++)
            {
                var value = c < cells.Count ? cells[c].Trim() : string.Empty;
                if (value.Length > 0)
                {
                    data[header[c]] = value.Length > 300 ? value[..300] : value;
                }
            }

            var row = new ImportRow { LegalEntityId = le.Id, BatchId = batch.Id, RowNumber = n + 1, Data = data };
            if (cells.Count > header.Count)
            {
                row.Errors.Add(new FieldError("row", "The row has more values than the header has columns."));
            }

            rows.Add(row);
        }

        await ValidateAsync(le.Id, rows, ct);
        batch.TotalRows = rows.Count;
        batch.ErrorRows = rows.Count(x => x.Errors.Count > 0);
        db.ImportBatches.Add(batch);
        db.ImportRows.AddRange(rows);
        audit.Record(db, "people.import.uploaded", "import_batch", batch.Id,
            after: AuditSnapshot.Of(new { batch.FileName, batch.FileSha256, batch.TotalRows, batch.ErrorRows }), legalEntityId: le.Id);
        await db.SaveChangesAsync(ct);
        return Map(batch);
    }

    public async Task<PagedResult<ImportBatchDto>> ListAsync(Guid? legalEntityId, int? page, int? pageSize, CancellationToken ct)
    {
        var scope = await ReadScopeAsync(ct);
        var (p, s) = Paging.Normalize(page, pageSize);
        var q = db.ImportBatches.AsNoTracking();
        if (!scope.AllLegalEntities)
        {
            var ids = scope.LegalEntityIds.ToList();
            q = q.Where(b => ids.Contains(b.LegalEntityId));
        }

        if (legalEntityId is { } le)
        {
            q = q.Where(b => b.LegalEntityId == le);
        }

        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(b => b.UploadedAt).Skip((p - 1) * s).Take(s).ToListAsync(ct);
        return new PagedResult<ImportBatchDto>(rows.Select(Map).ToList(), p, s, total);
    }

    public async Task<ImportBatchDto> GetAsync(Guid id, CancellationToken ct) => Map(await LoadAsync(id, ct));

    public async Task<PagedResult<ImportRowDto>> RowsAsync(Guid id, bool? onlyErrors, int? page, int? pageSize, CancellationToken ct)
    {
        var batch = await LoadAsync(id, ct);
        var (p, s) = Paging.Normalize(page, pageSize);
        var rows = await db.ImportRows.AsNoTracking().Where(r => r.BatchId == batch.Id).OrderBy(r => r.RowNumber).ToListAsync(ct);
        if (onlyErrors == true)
        {
            rows = rows.Where(r => r.Errors.Count > 0).ToList();
        }

        return new PagedResult<ImportRowDto>(rows.Skip((p - 1) * s).Take(s).Select(r => new ImportRowDto(
            r.RowNumber,
            r.Data.GetValueOrDefault("employee_number"),
            string.Join(' ', new[] { r.Data.GetValueOrDefault("first_name"), r.Data.GetValueOrDefault("last_name") }.Where(x => !string.IsNullOrEmpty(x))),
            r.Errors,
            r.EmployeeId)).ToList(), p, s, rows.Count);
    }

    public async Task<ImportBatchDto> DiscardAsync(Guid id, CancellationToken ct)
    {
        var batch = await LoadAsync(id, ct);
        await access.RequireAsync(Permissions.PeopleWrite, batch.LegalEntityId, ct);
        if (batch.Status != ImportStatuses.Validated)
        {
            throw new BusinessRuleException("not_open", "Only a batch that has not been committed can be discarded.");
        }

        var before = AuditSnapshot.Of(batch);
        batch.Status = ImportStatuses.Discarded;
        batch.DiscardedAt = clock.GetUtcNow();
        audit.Record(db, "people.import.discarded", "import_batch", batch.Id, before, AuditSnapshot.Of(batch), legalEntityId: batch.LegalEntityId);
        await db.SaveChangesAsync(ct);
        return Map(batch);
    }

    /// <summary>
    /// Creates every employee of the batch in one transaction. The committer must be a different
    /// user from the uploader: the commit also approves the batch's payment details. Committing a
    /// batch that is already committed returns it unchanged.
    /// </summary>
    public async Task<ImportBatchDto> CommitAsync(Guid id, CancellationToken ct)
    {
        var peek = await LoadAsync(id, ct);
        await access.RequireAsync(Permissions.PeopleWrite, peek.LegalEntityId, ct);

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtextextended({"people.import:" + peek.LegalEntityId}, 0))", ct);
            var batch = await db.ImportBatches.SingleAsync(b => b.Id == id, ct);
            await db.Entry(batch).ReloadAsync(ct);
            if (batch.Status == ImportStatuses.Committed)
            {
                return Map(batch);
            }

            if (batch.Status != ImportStatuses.Validated)
            {
                throw new BusinessRuleException("not_open", "This batch was discarded.");
            }

            if (batch.UploadedByUserId == context.ActorUserId)
            {
                throw new BusinessRuleException("self_approval", "Someone other than the uploader must review and commit the import.");
            }

            if (batch.ErrorRows > 0)
            {
                throw new BusinessRuleException("has_errors", "The file has rows with errors. Fix them and upload the file again; nothing was imported.");
            }

            var rows = await db.ImportRows.Where(r => r.BatchId == id).OrderBy(r => r.RowNumber).ToListAsync(ct);
            foreach (var row in rows)
            {
                row.Errors = [];
            }

            var drafts = await ValidateAsync(batch.LegalEntityId, rows, ct);
            batch.ErrorRows = rows.Count(r => r.Errors.Count > 0);
            if (batch.ErrorRows > 0)
            {
                audit.Record(db, "people.import.revalidated", "import_batch", batch.Id,
                    after: AuditSnapshot.Of(new { batch.TotalRows, batch.ErrorRows }), legalEntityId: batch.LegalEntityId);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                throw new BusinessRuleException("revalidation_failed",
                    $"{batch.ErrorRows} rows no longer pass validation (data changed since upload). Review the errors and upload a corrected file.");
            }

            var ids = drafts.ToDictionary(d => d.Number, _ => Guid.CreateVersion7());
            var existingManagers = await ExistingNumbersAsync(batch.LegalEntityId, ct);
            foreach (var d in drafts)
            {
                Guid? manager = d.ManagerNumber is null ? null : ids.TryGetValue(d.ManagerNumber, out var inFile) ? inFile : existingManagers[d.ManagerNumber];
                var assignment = d.Assignment with { ManagerEmployeeId = manager };
                var employee = employees.AddEmployee(batch.LegalEntityId, d.Number, d.HireDate, d.Personal, d.Identifiers, d.Contacts, assignment,
                    batch.Id, $"Import {batch.FileName} row {d.Row.RowNumber}", ids[d.Number]);
                var change = employees.AddPaymentChange(employee, d.Payment, "import", $"Import {batch.FileName} row {d.Row.RowNumber}", PaymentChangeStatuses.Approved, null);
                change.RequestedByUserId = batch.UploadedByUserId;
                change.DecidedByUserId = context.ActorUserId;
                change.DecidedAt = clock.GetUtcNow();
                change.DecisionNote = "Approved by committing the import batch";
                employee.PaymentMethod = d.Payment.PaymentMethod;
                employee.BankCode = d.Payment.BankCode;
                employee.BankBranchCode = d.Payment.BankBranchCode;
                employee.AccountNo = d.Payment.AccountNo;
                employee.AccountName = d.Payment.AccountName;
                employee.MpesaPhone = d.Payment.MpesaPhone;
                d.Row.EmployeeId = employee.Id;
            }

            var before = AuditSnapshot.Of(batch);
            batch.Status = ImportStatuses.Committed;
            batch.CommittedByUserId = context.ActorUserId;
            batch.CommittedAt = clock.GetUtcNow();
            audit.Record(db, "people.import.committed", "import_batch", batch.Id, before,
                AuditSnapshot.Of(new { batch.Status, batch.TotalRows, employeesCreated = drafts.Count, uploadedBy = batch.UploadedByUserId }),
                legalEntityId: batch.LegalEntityId);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Map(batch);
        }
    }

    // ---------- Row validation ----------

    internal sealed record Draft(ImportRow Row, string Number, DateOnly HireDate, Personal Personal, Identifiers Identifiers, List<Contact> Contacts,
        Assignment Assignment, string? ManagerNumber, PaymentDetails Payment);

    /// <summary>Validates every row, adding errors to the rows. Returns drafts only when no row has an error.</summary>
    internal async Task<List<Draft>> ValidateAsync(Guid legalEntityId, List<ImportRow> rows, CancellationToken ct)
    {
        var today = await employees.TodayAsync(legalEntityId, ct);
        var refs = await org.GetReferenceDataAsync(legalEntityId, ct);
        var branches = (await db.BankBranches.AsNoTracking().Where(b => b.IsActive).Select(b => new { b.BankCode, b.BranchCode }).ToListAsync(ct))
            .Select(b => (b.BankCode, b.BranchCode)).ToHashSet();
        var existing = await db.Employees.AsNoTracking().Where(x => x.LegalEntityId == legalEntityId)
            .Select(x => new { x.EmployeeNumber, x.NationalId, x.PassportNo, x.KraPin, x.NssfNo, x.ShaNo }).ToListAsync(ct);
        var existingManagers = await ExistingNumbersAsync(legalEntityId, ct);

        var drafts = new List<Draft>();
        var managerOf = new Dictionary<string, (ImportRow Row, string Manager)>();
        foreach (var row in rows)
        {
            var e = new FieldErrors();
            string? V(string column) => row.Data.GetValueOrDefault(column);
            DateOnly? D(string column)
            {
                var raw = V(column);
                if (raw is null)
                {
                    return null;
                }

                if (KenyanFormats.TryParseDate(raw, out var d))
                {
                    return d;
                }

                e.Add(column, "Use a date like 2026-03-31 or 31/03/2026.");
                return null;
            }

            var number = EmployeeRules.NormalizeEmployeeNumber(V("employee_number"), e, "employee_number");
            var hire = D("hire_date");
            if (hire is null && V("hire_date") is null)
            {
                e.Add("hire_date", "Hire date is required.");
            }

            var consent = V("intern_guardian_consent")?.ToLowerInvariant();
            if (consent is not null && consent is not ("yes" or "no" or "true" or "false" or "1" or "0" or "y" or "n"))
            {
                e.Add("intern_guardian_consent", "Use yes or no.");
            }

            var hireDate = hire ?? today;
            var ruleErrors = new FieldErrors();
            var assignment = EmployeeRules.NormalizeAssignment(new AssignmentInput(V("worker_category"), V("pay_basis"), V("job_title"),
                D("contract_end_date"), D("probation_end_date"), null, null, null, null, null, null, null), hireDate, ruleErrors, string.Empty);
            var personal = EmployeeRules.NormalizePersonal(new PersonalInput(V("first_name"), V("middle_name"), V("last_name"), D("date_of_birth"),
                V("gender"), V("citizenship"), V("residency_status"), V("phone_mobile"), V("email_work"), V("email_personal"),
                consent is "yes" or "true" or "1" or "y"), hireDate, assignment?.WorkerCategory ?? V("worker_category"), ruleErrors, string.Empty);
            var identifiers = EmployeeRules.NormalizeIdentifiers(new IdentifiersInput(V("national_id"), V("passport_no"), V("work_permit_no"),
                D("work_permit_expiry"), V("kra_pin"), V("nssf_no"), V("sha_no"), V("helb_ref"), V("disability_cert_no"), D("disability_cert_expiry")),
                personal?.Citizenship ?? V("citizenship")?.ToLowerInvariant(), today, ruleErrors, string.Empty);
            var contactInputs = new List<EmergencyContactInput> { new(V("emergency_contact_name"), V("emergency_contact_relationship"), V("emergency_contact_phone")) };
            if (V("emergency_contact2_name") is not null || V("emergency_contact2_phone") is not null || V("emergency_contact2_relationship") is not null)
            {
                contactInputs.Add(new EmergencyContactInput(V("emergency_contact2_name"), V("emergency_contact2_relationship"), V("emergency_contact2_phone")));
            }

            var contacts = EmployeeRules.NormalizeContacts(contactInputs, ruleErrors, "contacts");
            var payment = EmployeeRules.NormalizePayment(new PaymentDetailsInput(V("payment_method"), V("bank_code"), V("bank_branch_code"),
                V("account_no"), V("account_name"), V("mpesa_phone")), ruleErrors, string.Empty);
            foreach (var err in ruleErrors.Items)
            {
                var column = ColumnFor(err.Field);
                if (!e.Items.Any(x => x.Field == column))
                {
                    e.Add(column, err.Message);
                }
            }

            Guid? Ref(string kind, string column)
            {
                var code = V(column);
                if (code is null)
                {
                    return null;
                }

                var match = refs.ByCode(kind, code);
                if (match is not { IsActive: true })
                {
                    e.Add(column, $"No active {kind.Replace('_', ' ')} with code '{code}' in this employer.");
                    return null;
                }

                return match.Id;
            }

            var resolved = assignment is null ? null : assignment with
            {
                DepartmentId = Ref(OrgReferenceData.Departments, "department_code"),
                PositionId = Ref(OrgReferenceData.Positions, "position_code"),
                BranchId = Ref(OrgReferenceData.Branches, "branch_code"),
                CostCenterId = Ref(OrgReferenceData.CostCenters, "cost_center_code"),
                GradeId = Ref(OrgReferenceData.Grades, "grade_code"),
                WorkScheduleId = Ref(OrgReferenceData.WorkSchedules, "work_schedule_code"),
            };

            if (payment is { PaymentMethod: PaymentMethods.Bank } && !branches.Contains((payment.BankCode!, payment.BankBranchCode!)))
            {
                e.Add("bank_branch_code", "Bank and branch are not in the bank reference table.");
            }

            if (number is not null && identifiers is not null)
            {
                foreach (var m in existing)
                {
                    if (m.EmployeeNumber == number) e.Add("employee_number", "This employee number already exists for this employer.");
                    if (identifiers.NationalId is not null && m.NationalId == identifiers.NationalId) e.Add("national_id", $"Already recorded for employee {m.EmployeeNumber}.");
                    if (identifiers.PassportNo is not null && m.PassportNo == identifiers.PassportNo) e.Add("passport_no", $"Already recorded for employee {m.EmployeeNumber}.");
                    if (identifiers.KraPin is not null && m.KraPin == identifiers.KraPin) e.Add("kra_pin", $"Already recorded for employee {m.EmployeeNumber}.");
                    if (identifiers.NssfNo is not null && m.NssfNo == identifiers.NssfNo) e.Add("nssf_no", $"Already recorded for employee {m.EmployeeNumber}.");
                    if (identifiers.ShaNo is not null && m.ShaNo == identifiers.ShaNo) e.Add("sha_no", $"Already recorded for employee {m.EmployeeNumber}.");
                }
            }

            var managerNumber = V("manager_employee_number")?.ToUpperInvariant();
            if (number is not null && managerNumber is not null)
            {
                if (managerNumber == number)
                {
                    e.Add("manager_employee_number", "An employee cannot be their own manager.");
                }
                else
                {
                    managerOf[number] = (row, managerNumber);
                }
            }

            row.Errors.AddRange(e.Items);
            if (row.Errors.Count == 0)
            {
                drafts.Add(new Draft(row, number!, hireDate, personal!, identifiers!, contacts!, resolved!, managerNumber, payment!));
            }
        }

        // Values that must be unique within the employer may not repeat inside the file either.
        void Unique(string column, Func<ImportRow, string?> value)
        {
            foreach (var group in rows.Select(r => (Row: r, Value: value(r))).Where(x => x.Value is not null).GroupBy(x => x.Value).Where(g => g.Count() > 1))
            {
                foreach (var item in group)
                {
                    var others = string.Join(", ", group.Where(o => o.Row != item.Row).Select(o => o.Row.RowNumber));
                    item.Row.Errors.Add(new FieldError(column, $"The same value is also on row {others}."));
                }
            }
        }

        Unique("employee_number", r => r.Data.GetValueOrDefault("employee_number")?.Trim().ToUpperInvariant());
        Unique("national_id", r => r.Data.GetValueOrDefault("national_id")?.Trim());
        Unique("passport_no", r => r.Data.GetValueOrDefault("passport_no")?.Trim().ToUpperInvariant());
        Unique("kra_pin", r => r.Data.GetValueOrDefault("kra_pin")?.Trim().ToUpperInvariant());
        Unique("nssf_no", r => r.Data.GetValueOrDefault("nssf_no")?.Trim());
        Unique("sha_no", r => r.Data.GetValueOrDefault("sha_no")?.Trim().ToUpperInvariant());

        // Managers: an existing employee, or another valid row; no loops inside the file.
        var fileNumbers = rows.Select(r => r.Data.GetValueOrDefault("employee_number")?.Trim().ToUpperInvariant()).Where(n => n is not null).ToHashSet();
        var validNumbers = rows.Where(r => r.Errors.Count == 0).Select(r => r.Data["employee_number"].Trim().ToUpperInvariant()).ToHashSet();
        var graph = managerOf.ToDictionary(kv => kv.Key, kv => (string?)kv.Value.Manager);
        foreach (var (number, (row, manager)) in managerOf)
        {
            if (fileNumbers.Contains(manager))
            {
                if (!validNumbers.Contains(manager))
                {
                    row.Errors.Add(new FieldError("manager_employee_number", $"The manager's row ({manager}) has errors."));
                }
                else if (InFileCycle(number, graph))
                {
                    row.Errors.Add(new FieldError("manager_employee_number", "The managers in this file form a loop."));
                }
            }
            else if (!existingManagers.ContainsKey(manager))
            {
                row.Errors.Add(new FieldError("manager_employee_number", $"No current employee {manager} in this employer or this file."));
            }
        }

        return rows.Any(r => r.Errors.Count > 0) ? [] : drafts;
    }

    private static bool InFileCycle(string start, Dictionary<string, string?> graph)
    {
        var seen = new HashSet<string>();
        var current = start;
        while (graph.TryGetValue(current, out var next) && next is not null)
        {
            if (next == start || !seen.Add(next))
            {
                return true;
            }

            current = next;
        }

        return false;
    }

    /// <summary>Employee numbers of people currently employed by the employer (possible managers).</summary>
    private async Task<Dictionary<string, Guid>> ExistingNumbersAsync(Guid legalEntityId, CancellationToken ct)
    {
        var today = await employees.TodayAsync(legalEntityId, ct);
        return await (
            from e in db.Employees.AsNoTracking()
            where e.LegalEntityId == legalEntityId
                  && db.Episodes.Any(ep => ep.EmployeeId == e.Id && ep.HireDate <= today && (ep.TerminationDate == null || ep.TerminationDate >= today))
            select new { e.EmployeeNumber, e.Id }).ToDictionaryAsync(x => x.EmployeeNumber, x => x.Id, ct);
    }

    /// <summary>Maps a rule field ("dateOfBirth", "contacts[1].phone") to its CSV column.</summary>
    internal static string ColumnFor(string field)
    {
        var contact = ContactField().Match(field);
        if (contact.Success)
        {
            var prefix = contact.Groups[1].Value == "0" ? "emergency_contact_" : "emergency_contact2_";
            return prefix + contact.Groups[2].Value;
        }

        return field switch
        {
            "contacts" => "emergency_contact_name",
            _ => Snake().Replace(field, "_$1").ToLowerInvariant(),
        };
    }

    private async Task<ImportBatch> LoadAsync(Guid id, CancellationToken ct)
    {
        var scope = await ReadScopeAsync(ct);
        var batch = await db.ImportBatches.SingleOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null || !scope.Allows(batch.LegalEntityId))
        {
            throw new NotFoundException("Import batch");
        }

        return batch;
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

    private ImportBatchDto Map(ImportBatch b)
    {
        string? blocked = b.Status != ImportStatuses.Validated ? $"The batch is {b.Status}."
            : b.ErrorRows > 0 ? "Fix the rows with errors and upload the file again."
            : b.UploadedByUserId == context.ActorUserId ? "Another user must review and commit this import."
            : null;
        return new ImportBatchDto(b.Id, b.LegalEntityId, b.FileName, b.Status, b.TotalRows, b.ErrorRows, b.TotalRows - b.ErrorRows,
            b.UploadedByUserId, b.UploadedByDisplay, b.UploadedAt, b.CommittedByUserId, b.CommittedAt, blocked is null, blocked, b.Version);
    }

    [GeneratedRegex("^contacts\\[(\\d)\\]\\.(\\w+)$")]
    private static partial Regex ContactField();

    [GeneratedRegex("(?<=[a-z0-9])([A-Z])")]
    private static partial Regex Snake();
}
