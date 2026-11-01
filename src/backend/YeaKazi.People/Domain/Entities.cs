using YeaKazi.BuildingBlocks.Domain;

namespace YeaKazi.People.Domain;

/// <summary>
/// Employee master (EMP-01, EMP-03). One record per person per legal employer; the id is
/// immutable and the employee number is unique within the employer. Restricted identifiers
/// are masked in audit snapshots and shown only to holders of people.sensitive.read.
/// Payment details on this row change only through an approved PaymentDetailsChange.
/// </summary>
public sealed class Employee : IEmployerScoped, IVersioned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid LegalEntityId { get; set; }
    public string EmployeeNumber { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = string.Empty;
    [AuditMask] public DateOnly DateOfBirth { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string Citizenship { get; set; } = Citizenships.Kenyan;
    public string ResidencyStatus { get; set; } = "resident";
    public bool InternGuardianConsent { get; set; }

    [AuditMask] public string PhoneMobile { get; set; } = string.Empty;
    [AuditMask] public string? EmailWork { get; set; }
    [AuditMask] public string? EmailPersonal { get; set; }

    [AuditMask] public string? NationalId { get; set; }
    [AuditMask] public string? PassportNo { get; set; }
    [AuditMask] public string? WorkPermitNo { get; set; }
    public DateOnly? WorkPermitExpiry { get; set; }
    [AuditMask] public string? KraPin { get; set; }
    [AuditMask] public string? NssfNo { get; set; }
    [AuditMask] public string? ShaNo { get; set; }
    [AuditMask] public string? HelbRef { get; set; }
    [AuditMask] public string? DisabilityCertNo { get; set; }
    public DateOnly? DisabilityCertExpiry { get; set; }

    public string? PaymentMethod { get; set; }
    public string? BankCode { get; set; }
    public string? BankBranchCode { get; set; }
    [AuditMask] public string? AccountNo { get; set; }
    public string? AccountName { get; set; }
    [AuditMask] public string? MpesaPhone { get; set; }

    public Guid? ImportBatchId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? CreatedByUserId { get; set; }
    public uint Version { get; set; }

    public string FullName => string.Join(' ', new[] { FirstName, MiddleName, LastName }.Where(s => !string.IsNullOrEmpty(s)));
}

/// <summary>Emergency contact. Replaced contacts are deactivated, never deleted.</summary>
public sealed class EmergencyContact : IEmployerScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid LegalEntityId { get; set; }
    public Guid EmployeeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    [AuditMask] public string Phone { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>One period of employment (EMP-02). Rehire opens a new episode; earlier episodes are kept.</summary>
public sealed class EmploymentEpisode : IEmployerScoped, IVersioned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid LegalEntityId { get; set; }
    public Guid EmployeeId { get; set; }
    public int Sequence { get; set; }
    public DateOnly HireDate { get; set; }

    /// <summary>Last day of employment (inclusive). May be in the future while the employee serves notice.</summary>
    public DateOnly? TerminationDate { get; set; }
    public string? TerminationType { get; set; }
    public string? TerminationReason { get; set; }
    public DateTimeOffset? TerminationRecordedAt { get; set; }
    public Guid? TerminationRecordedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public uint Version { get; set; }
}

/// <summary>
/// Effective-dated assignment (EMP-02, EMP-04). A version applies from EffectiveFrom until the
/// day before the next non-cancelled version of the same episode, so periods cannot overlap.
/// Versions are never edited: a mistake is fixed by cancelling the version (kept, with the
/// reason) and recording a correcting version for the same date.
/// </summary>
public sealed class AssignmentVersion : IEmployerScoped, IVersioned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid LegalEntityId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid EpisodeId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public string ChangeType { get; set; } = ChangeTypes.Hire;
    public string Status { get; set; } = AssignmentStatuses.Active;
    public string WorkerCategory { get; set; } = WorkerCategories.Permanent;
    public string PayBasis { get; set; } = PayBases.Monthly;
    public string? JobTitle { get; set; }
    public DateOnly? ContractEndDate { get; set; }
    public DateOnly? ProbationEndDate { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? PositionId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? CostCenterId { get; set; }
    public Guid? GradeId { get; set; }
    public Guid? WorkScheduleId { get; set; }
    public Guid? ManagerEmployeeId { get; set; }
    public string? Reason { get; set; }
    public Guid? CorrectsVersionId { get; set; }
    public bool IsCancelled { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancelReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? CreatedByUserId { get; set; }
    public uint Version { get; set; }
}

/// <summary>
/// Proposed payment destination (bank, M-Pesa or cash). Takes effect only when a second user
/// approves it (Section 15: bank change requires approval; cash needs approval).
/// </summary>
public sealed class PaymentDetailsChange : IEmployerScoped, IVersioned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid LegalEntityId { get; set; }
    public Guid EmployeeId { get; set; }
    public string PaymentMethod { get; set; } = PaymentMethods.Bank;
    public string? BankCode { get; set; }
    public string? BankBranchCode { get; set; }
    [AuditMask] public string? AccountNo { get; set; }
    public string? AccountName { get; set; }
    [AuditMask] public string? MpesaPhone { get; set; }
    public string Source { get; set; } = "change";
    public string Status { get; set; } = PaymentChangeStatuses.Pending;
    public string? Reason { get; set; }
    public Guid? RequestedByUserId { get; set; }
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? DecidedByUserId { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public uint Version { get; set; }
}

/// <summary>Bank and branch reference table (tenant-maintained until an approved CBK code list is loaded).</summary>
public sealed class BankBranch : ITenantOwned, IVersioned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public string BankCode { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string BranchCode { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public uint Version { get; set; }
}

/// <summary>MIG-01 employee import: validated preview, then committed by a second user.</summary>
public sealed class ImportBatch : IEmployerScoped, IVersioned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid LegalEntityId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FileSha256 { get; set; } = string.Empty;
    public string Status { get; set; } = ImportStatuses.Validated;
    public int TotalRows { get; set; }
    public int ErrorRows { get; set; }
    public Guid? UploadedByUserId { get; set; }
    public string? UploadedByDisplay { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? CommittedByUserId { get; set; }
    public DateTimeOffset? CommittedAt { get; set; }
    public DateTimeOffset? DiscardedAt { get; set; }
    public uint Version { get; set; }
}

public sealed class ImportRow : IEmployerScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid LegalEntityId { get; set; }
    public Guid BatchId { get; set; }
    public int RowNumber { get; set; }

    /// <summary>Raw cell values by column. Holds restricted data, so it is never copied into audit events.</summary>
    [AuditIgnore] public Dictionary<string, string> Data { get; set; } = new();
    public List<FieldError> Errors { get; set; } = [];
    public Guid? EmployeeId { get; set; }
}

public sealed record FieldError(string Field, string Message);
