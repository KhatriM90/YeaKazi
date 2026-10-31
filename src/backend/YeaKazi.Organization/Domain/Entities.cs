using YeaKazi.BuildingBlocks.Domain;

namespace YeaKazi.Organization.Domain;

/// <summary>Legal employer (ORG-01). Each employer has separate payroll and statutory records.</summary>
public sealed class LegalEntity : ITenantOwned, IVersioned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? RegistrationNumber { get; set; }
    public string? KraPin { get; set; }
    public string? NssfEmployerNumber { get; set; }
    public string Timezone { get; set; } = "Africa/Nairobi";
    public string BaseCurrency { get; set; } = "KES";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public uint Version { get; set; }
}

/// <summary>Common shape of employer-scoped organization records.</summary>
public abstract class EmployerRecord : IEmployerScoped, IVersioned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid LegalEntityId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public uint Version { get; set; }
}

public sealed class Branch : EmployerRecord
{
    public string? Address { get; set; }
}

public sealed class CostCenter : EmployerRecord
{
}

public sealed class Grade : EmployerRecord
{
    /// <summary>Ordering of grades (1 = most junior). Not a pay value.</summary>
    public int Rank { get; set; }
}

public sealed class Department : EmployerRecord
{
    public Guid? ParentDepartmentId { get; set; }
    public Guid? CostCenterId { get; set; }
}

/// <summary>Position; reporting lines are position-to-position (ReportsToPositionId).</summary>
public sealed class Position : EmployerRecord
{
    public Guid? DepartmentId { get; set; }
    public Guid? GradeId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? ReportsToPositionId { get; set; }
}

public sealed class HolidayCalendar : EmployerRecord
{
}

public static class HolidayKinds
{
    public const string Gazetted = "gazetted";
    public const string ShortNotice = "short_notice";
    public const string Company = "company";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Gazetted, ShortNotice, Company };
}

public sealed class Holiday : IEmployerScoped, IVersioned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid LegalEntityId { get; set; }
    public Guid CalendarId { get; set; }
    public DateOnly Date { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = HolidayKinds.Company;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public uint Version { get; set; }
}

/// <summary>Working schedule. EndTime at or before StartTime means an overnight shift.</summary>
public sealed class WorkSchedule : EmployerRecord
{
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int BreakMinutes { get; set; }

    /// <summary>ISO weekdays, 1 = Monday ... 7 = Sunday.</summary>
    public int[] WorkingDays { get; set; } = [];

    public bool IsOvernight => EndTime <= StartTime;

    public int ScheduledMinutesPerDay
    {
        get
        {
            var minutes = (int)(EndTime.ToTimeSpan() - StartTime.ToTimeSpan()).TotalMinutes;
            if (minutes <= 0)
            {
                minutes += 24 * 60;
            }

            return minutes - BreakMinutes;
        }
    }
}
