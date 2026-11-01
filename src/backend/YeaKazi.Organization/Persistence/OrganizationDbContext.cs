using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YeaKazi.BuildingBlocks.Persistence;
using YeaKazi.BuildingBlocks.Tenancy;
using YeaKazi.Organization.Domain;

namespace YeaKazi.Organization.Persistence;

/// <summary>
/// Organization module (schema "org"). Composite foreign keys on (tenant_id, legal_entity_id, id)
/// make cross-tenant and cross-employer references impossible at the database level.
/// </summary>
public sealed class OrganizationDbContext(DbContextOptions<OrganizationDbContext> options, RequestContext requestContext, SharedDbConnection? shared = null)
    : ModuleDbContext(options, requestContext, shared)
{
    public const string Schema = "org";

    public DbSet<LegalEntity> LegalEntities => Set<LegalEntity>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<CostCenter> CostCenters => Set<CostCenter>();
    public DbSet<Grade> Grades => Set<Grade>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<HolidayCalendar> HolidayCalendars => Set<HolidayCalendar>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<WorkSchedule> WorkSchedules => Set<WorkSchedule>();

    protected override bool OwnsSharedTables => false;

    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LegalEntity>(b =>
        {
            b.ToTable("legal_entities", Schema);
            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.TenantId, x.Id });
            b.Property(x => x.Code).HasMaxLength(20).IsRequired();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.RegistrationNumber).HasMaxLength(50);
            b.Property(x => x.KraPin).HasMaxLength(11);
            b.Property(x => x.NssfEmployerNumber).HasMaxLength(20);
            b.Property(x => x.Timezone).HasMaxLength(64).IsRequired();
            b.Property(x => x.BaseCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.KraPin }).IsUnique().HasFilter("kra_pin is not null");
        });

        Employer<Branch>(modelBuilder, "branches", b => b.Property(x => x.Address).HasMaxLength(400));
        Employer<CostCenter>(modelBuilder, "cost_centers", _ => { });
        Employer<Grade>(modelBuilder, "grades", _ => { });
        Employer<HolidayCalendar>(modelBuilder, "holiday_calendars", _ => { });

        Employer<Department>(modelBuilder, "departments", b =>
        {
            b.HasOne<Department>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.LegalEntityId, x.ParentDepartmentId })
                .HasPrincipalKey(x => new { x.TenantId, x.LegalEntityId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne<CostCenter>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.LegalEntityId, x.CostCenterId })
                .HasPrincipalKey(x => new { x.TenantId, x.LegalEntityId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("ck_departments_not_own_parent", "parent_department_id is null or parent_department_id <> id"));
        });

        Employer<Position>(modelBuilder, "positions", b =>
        {
            b.HasOne<Department>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.LegalEntityId, x.DepartmentId })
                .HasPrincipalKey(x => new { x.TenantId, x.LegalEntityId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Grade>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.LegalEntityId, x.GradeId })
                .HasPrincipalKey(x => new { x.TenantId, x.LegalEntityId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Branch>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.LegalEntityId, x.BranchId })
                .HasPrincipalKey(x => new { x.TenantId, x.LegalEntityId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Position>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.LegalEntityId, x.ReportsToPositionId })
                .HasPrincipalKey(x => new { x.TenantId, x.LegalEntityId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("ck_positions_not_own_manager", "reports_to_position_id is null or reports_to_position_id <> id"));
        });

        Employer<WorkSchedule>(modelBuilder, "work_schedules", b =>
        {
            b.Property(x => x.WorkingDays).HasColumnType("integer[]");
            b.ToTable(t =>
            {
                t.HasCheckConstraint("ck_work_schedules_break", "break_minutes >= 0 and break_minutes < 1440");
                t.HasCheckConstraint("ck_work_schedules_days", "working_days <@ array[1,2,3,4,5,6,7] and cardinality(working_days) between 1 and 7");
            });
        });

        modelBuilder.Entity<Holiday>(b =>
        {
            b.ToTable("holidays", Schema);
            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.TenantId, x.LegalEntityId, x.Id });
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Kind).HasMaxLength(20).IsRequired();
            b.HasOne<LegalEntity>().WithMany().HasForeignKey(x => new { x.TenantId, x.LegalEntityId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<HolidayCalendar>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.LegalEntityId, x.CalendarId })
                .HasPrincipalKey(x => new { x.TenantId, x.LegalEntityId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.CalendarId, x.Date }).IsUnique().HasFilter("is_active");
        });
    }

    private static void Employer<T>(ModelBuilder modelBuilder, string table, Action<EntityTypeBuilder<T>> extra)
        where T : EmployerRecord
    {
        modelBuilder.Entity<T>(b =>
        {
            b.ToTable(table, Schema);
            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.TenantId, x.LegalEntityId, x.Id });
            b.Property(x => x.Code).HasMaxLength(20).IsRequired();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Code }).IsUnique();
            b.HasOne<LegalEntity>().WithMany().HasForeignKey(x => new { x.TenantId, x.LegalEntityId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            extra(b);
        });
    }
}
