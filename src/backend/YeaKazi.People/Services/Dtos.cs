using Microsoft.AspNetCore.Mvc;
using YeaKazi.People.Domain;

namespace YeaKazi.People.Services;

public sealed record EmployeeListQuery(
    [property: FromQuery(Name = "legalEntityId")] Guid? LegalEntityId,
    [property: FromQuery(Name = "search")] string? Search,
    [property: FromQuery(Name = "status")] string? Status,
    [property: FromQuery(Name = "departmentId")] Guid? DepartmentId,
    [property: FromQuery(Name = "page")] int? Page,
    [property: FromQuery(Name = "pageSize")] int? PageSize);

public sealed record EmployeeSummaryDto(
    Guid Id, Guid LegalEntityId, string EmployeeNumber, string FullName, string Status, string? JobTitle, string? WorkerCategory,
    Guid? DepartmentId, Guid? PositionId, Guid? BranchId, DateOnly? HireDate, DateOnly? TerminationDate, IReadOnlyList<string> StatutoryGaps);

public sealed record PersonalDto(
    string FirstName, string? MiddleName, string LastName, string FullName, DateOnly DateOfBirth, string Gender, string Citizenship,
    string ResidencyStatus, string PhoneMobile, string? EmailWork, string? EmailPersonal, bool InternGuardianConsent);

/// <summary>Restricted identifiers. Masked ("***89Z") unless the caller holds people.sensitive.read for the employer.</summary>
public sealed record IdentifiersDto(
    string? NationalId, string? PassportNo, string? WorkPermitNo, DateOnly? WorkPermitExpiry, string? KraPin, string? NssfNo,
    string? ShaNo, string? HelbRef, string? DisabilityCertNo, DateOnly? DisabilityCertExpiry);

public sealed record EmergencyContactDto(Guid Id, string Name, string Relationship, string Phone);

public sealed record PaymentDetailsDto(string PaymentMethod, string? BankCode, string? BankBranchCode, string? AccountNo, string? AccountName, string? MpesaPhone);

public sealed record EpisodeDto(Guid Id, int Sequence, DateOnly HireDate, DateOnly? TerminationDate, string? TerminationType, string? TerminationReason, uint Version);

public sealed record AssignmentDto(
    Guid Id, Guid EpisodeId, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string ChangeType, string Status, string WorkerCategory, string PayBasis,
    string? JobTitle, DateOnly? ContractEndDate, DateOnly? ProbationEndDate, Guid? DepartmentId, Guid? PositionId, Guid? BranchId,
    Guid? CostCenterId, Guid? GradeId, Guid? WorkScheduleId, Guid? ManagerEmployeeId, string? ManagerName, string? Reason,
    Guid? CorrectsVersionId, bool IsCancelled, string? CancelReason, DateTimeOffset? CancelledAt, DateTimeOffset CreatedAt, uint Version);

public sealed record PaymentChangeDto(
    Guid Id, Guid EmployeeId, string EmployeeNumber, string EmployeeName, Guid LegalEntityId, string PaymentMethod, string? BankCode,
    string? BankBranchCode, string? AccountNo, string? AccountName, string? MpesaPhone, string Source, string Status, string? Reason,
    Guid? RequestedByUserId, DateTimeOffset RequestedAt, Guid? DecidedByUserId, DateTimeOffset? DecidedAt, string? DecisionNote,
    IReadOnlyList<string> Warnings, uint Version);

public sealed record EmployeeDto(
    Guid Id, Guid LegalEntityId, string EmployeeNumber, string Status, PersonalDto Personal, IdentifiersDto Identifiers, bool SensitiveMasked,
    IReadOnlyList<EmergencyContactDto> EmergencyContacts, PaymentDetailsDto? PaymentDetails, PaymentChangeDto? PendingPaymentChange,
    EpisodeDto? CurrentEpisode, AssignmentDto? CurrentAssignment, IReadOnlyList<string> StatutoryGaps, string? WorkPermitAlert,
    Guid? ImportBatchId, uint Version, DateTimeOffset CreatedAt);

public sealed record EpisodeHistoryDto(EpisodeDto Episode, IReadOnlyList<AssignmentDto> Versions);

public sealed record EmployeeHistoryDto(Guid EmployeeId, DateOnly Today, IReadOnlyList<EpisodeHistoryDto> Episodes);

public sealed record CreateEmployeeRequest(
    Guid LegalEntityId, string? EmployeeNumber, DateOnly? HireDate, PersonalInput? Personal, IdentifiersInput? Identifiers,
    IReadOnlyList<EmergencyContactInput>? EmergencyContacts, AssignmentInput? Assignment, PaymentDetailsInput? PaymentDetails);

/// <summary>Identifiers may be sent only by holders of people.sensitive.read; omit them to keep the stored values.</summary>
public sealed record UpdateEmployeeRequest(PersonalInput? Personal, IdentifiersInput? Identifiers, uint Version, string? Reason);

public sealed record ReplaceEmergencyContactsRequest(IReadOnlyList<EmergencyContactInput>? Contacts, string? Reason);

public sealed record AddAssignmentRequest(DateOnly EffectiveFrom, string? ChangeType, AssignmentInput? Assignment, string? Reason);

public sealed record CorrectAssignmentRequest(AssignmentInput? Assignment, string? Reason, uint Version);

public sealed record CancelAssignmentRequest(string? Reason, uint Version);

public sealed record TerminateRequest(DateOnly TerminationDate, string? TerminationType, string? Reason, uint Version);

public sealed record RescindTerminationRequest(string? Reason, uint Version);

public sealed record RehireRequest(DateOnly HireDate, AssignmentInput? Assignment, string? Reason);

public sealed record RequestPaymentChangeRequest(PaymentDetailsInput? Details, string? Reason);

public sealed record DecidePaymentChangeRequest(string? Note, uint Version);

public sealed record WorkPermitAlertDto(Guid EmployeeId, string EmployeeNumber, string FullName, Guid LegalEntityId, DateOnly WorkPermitExpiry, int DaysLeft, string Alert);

public sealed record BankBranchDto(Guid Id, string BankCode, string BankName, string BranchCode, string BranchName, bool IsActive, uint Version);

public sealed record CreateBankBranchRequest(string? BankCode, string? BankName, string? BranchCode, string? BranchName);

public sealed record UpdateBankBranchRequest(string? BankName, string? BranchName, bool IsActive, uint Version, string? Reason);
