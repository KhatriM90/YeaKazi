namespace YeaKazi.People.Domain;

public sealed record PersonalInput(
    string? FirstName,
    string? MiddleName,
    string? LastName,
    DateOnly? DateOfBirth,
    string? Gender,
    string? Citizenship,
    string? ResidencyStatus,
    string? PhoneMobile,
    string? EmailWork,
    string? EmailPersonal,
    bool InternGuardianConsent);

public sealed record IdentifiersInput(
    string? NationalId,
    string? PassportNo,
    string? WorkPermitNo,
    DateOnly? WorkPermitExpiry,
    string? KraPin,
    string? NssfNo,
    string? ShaNo,
    string? HelbRef,
    string? DisabilityCertNo,
    DateOnly? DisabilityCertExpiry);

public sealed record EmergencyContactInput(string? Name, string? Relationship, string? Phone);

public sealed record AssignmentInput(
    string? WorkerCategory,
    string? PayBasis,
    string? JobTitle,
    DateOnly? ContractEndDate,
    DateOnly? ProbationEndDate,
    Guid? DepartmentId,
    Guid? PositionId,
    Guid? BranchId,
    Guid? CostCenterId,
    Guid? GradeId,
    Guid? WorkScheduleId,
    Guid? ManagerEmployeeId);

public sealed record PaymentDetailsInput(
    string? PaymentMethod,
    string? BankCode,
    string? BankBranchCode,
    string? AccountNo,
    string? AccountName,
    string? MpesaPhone);

// Normalized values produced by EmployeeRules.

public sealed record Personal(string FirstName, string? MiddleName, string LastName, DateOnly DateOfBirth, string Gender, string Citizenship,
    string ResidencyStatus, string PhoneMobile, string? EmailWork, string? EmailPersonal, bool InternGuardianConsent);

public sealed record Identifiers(string? NationalId, string? PassportNo, string? WorkPermitNo, DateOnly? WorkPermitExpiry, string? KraPin,
    string? NssfNo, string? ShaNo, string? HelbRef, string? DisabilityCertNo, DateOnly? DisabilityCertExpiry);

public sealed record Contact(string Name, string Relationship, string Phone);

public sealed record Assignment(string WorkerCategory, string PayBasis, string? JobTitle, DateOnly? ContractEndDate, DateOnly? ProbationEndDate,
    Guid? DepartmentId, Guid? PositionId, Guid? BranchId, Guid? CostCenterId, Guid? GradeId, Guid? WorkScheduleId, Guid? ManagerEmployeeId);

public sealed record PaymentDetails(string PaymentMethod, string? BankCode, string? BankBranchCode, string? AccountNo, string? AccountName, string? MpesaPhone);
