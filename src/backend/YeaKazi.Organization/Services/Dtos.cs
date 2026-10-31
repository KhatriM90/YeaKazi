namespace YeaKazi.Organization.Services;

public sealed record OrgListQuery(Guid? LegalEntityId, bool? IncludeInactive, string? Search, int? Page, int? PageSize);

public sealed record LegalEntityDto(Guid Id, string Code, string Name, string? RegistrationNumber, string? KraPin, string? NssfEmployerNumber, string Timezone, string BaseCurrency, bool IsActive, uint Version);
public sealed record CreateLegalEntityRequest(string Code, string Name, string? RegistrationNumber, string? KraPin, string? NssfEmployerNumber, string? Timezone);
public sealed record UpdateLegalEntityRequest(string Name, string? RegistrationNumber, string? KraPin, string? NssfEmployerNumber, string Timezone, bool IsActive, uint Version, string? Reason);

public sealed record BranchDto(Guid Id, Guid LegalEntityId, string Code, string Name, string? Address, bool IsActive, uint Version);
public sealed record CreateBranchRequest(Guid LegalEntityId, string Code, string Name, string? Address);
public sealed record UpdateBranchRequest(string Name, string? Address, bool IsActive, uint Version, string? Reason);

public sealed record CostCenterDto(Guid Id, Guid LegalEntityId, string Code, string Name, bool IsActive, uint Version);
public sealed record CreateCostCenterRequest(Guid LegalEntityId, string Code, string Name);
public sealed record UpdateCostCenterRequest(string Name, bool IsActive, uint Version, string? Reason);

public sealed record GradeDto(Guid Id, Guid LegalEntityId, string Code, string Name, int Rank, bool IsActive, uint Version);
public sealed record CreateGradeRequest(Guid LegalEntityId, string Code, string Name, int Rank);
public sealed record UpdateGradeRequest(string Name, int Rank, bool IsActive, uint Version, string? Reason);

public sealed record DepartmentDto(Guid Id, Guid LegalEntityId, string Code, string Name, Guid? ParentDepartmentId, Guid? CostCenterId, bool IsActive, uint Version);
public sealed record CreateDepartmentRequest(Guid LegalEntityId, string Code, string Name, Guid? ParentDepartmentId, Guid? CostCenterId);
public sealed record UpdateDepartmentRequest(string Name, Guid? ParentDepartmentId, Guid? CostCenterId, bool IsActive, uint Version, string? Reason);

public sealed record PositionDto(Guid Id, Guid LegalEntityId, string Code, string Name, Guid? DepartmentId, Guid? GradeId, Guid? BranchId, Guid? ReportsToPositionId, bool IsActive, uint Version);
public sealed record CreatePositionRequest(Guid LegalEntityId, string Code, string Name, Guid? DepartmentId, Guid? GradeId, Guid? BranchId, Guid? ReportsToPositionId);
public sealed record UpdatePositionRequest(string Name, Guid? DepartmentId, Guid? GradeId, Guid? BranchId, Guid? ReportsToPositionId, bool IsActive, uint Version, string? Reason);

public sealed record HolidayCalendarDto(Guid Id, Guid LegalEntityId, string Code, string Name, bool IsActive, uint Version);
public sealed record CreateHolidayCalendarRequest(Guid LegalEntityId, string Code, string Name);
public sealed record UpdateHolidayCalendarRequest(string Name, bool IsActive, uint Version, string? Reason);

public sealed record HolidayDto(Guid Id, Guid CalendarId, DateOnly Date, string Name, string Kind, bool IsActive, uint Version);
public sealed record CreateHolidayRequest(DateOnly Date, string Name, string Kind);
public sealed record UpdateHolidayRequest(string Name, string Kind, bool IsActive, uint Version, string? Reason);

public sealed record WorkScheduleDto(Guid Id, Guid LegalEntityId, string Code, string Name, string StartTime, string EndTime, int BreakMinutes, IReadOnlyList<int> WorkingDays, bool IsOvernight, int ScheduledMinutesPerDay, bool IsActive, uint Version);
public sealed record CreateWorkScheduleRequest(Guid LegalEntityId, string Code, string Name, string StartTime, string EndTime, int BreakMinutes, IReadOnlyList<int> WorkingDays);
public sealed record UpdateWorkScheduleRequest(string Name, string StartTime, string EndTime, int BreakMinutes, IReadOnlyList<int> WorkingDays, bool IsActive, uint Version, string? Reason);
