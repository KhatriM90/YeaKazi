using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YeaKazi.BuildingBlocks.Domain;
using YeaKazi.BuildingBlocks.Persistence;
using YeaKazi.BuildingBlocks.Tenancy;
using YeaKazi.People.Domain;

namespace YeaKazi.People.Persistence;

/// <summary>
/// People module (schema "people"). Children reference employees through composite keys
/// (tenant_id, legal_entity_id, id), so a record can never point into another tenant or employer.
/// Foreign keys into the Organization schema are added in SQL (PeopleSecurity).
/// </summary>
public sealed class PeopleDbContext(DbContextOptions<PeopleDbContext> options, RequestContext requestContext, SharedDbConnection? shared = null)
    : ModuleDbContext(options, requestContext, shared)
{
    public const string Schema = "people";

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmergencyContact> EmergencyContacts => Set<EmergencyContact>();
    public DbSet<EmploymentEpisode> Episodes => Set<EmploymentEpisode>();
    public DbSet<AssignmentVersion> Assignments => Set<AssignmentVersion>();
    public DbSet<PaymentDetailsChange> PaymentChanges => Set<PaymentDetailsChange>();
    public DbSet<BankBranch> BankBranches => Set<BankBranch>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportRow> ImportRows => Set<ImportRow>();

    protected override bool OwnsSharedTables => false;

    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Employee>(b =>
        {
            b.ToTable("employees", Schema, t =>
            {
                t.HasCheckConstraint("ck_employees_gender", "gender in ('female','male','intersex')");
                t.HasCheckConstraint("ck_employees_citizenship", "citizenship in ('kenyan','non_kenyan')");
                t.HasCheckConstraint("ck_employees_residency", "residency_status in ('resident','non_resident')");
                t.HasCheckConstraint("ck_employees_payment_method", "payment_method is null or payment_method in ('bank','mpesa','cash')");
                t.HasCheckConstraint("ck_employees_identity", "national_id is not null or passport_no is not null");
            });
            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.TenantId, x.LegalEntityId, x.Id });
            b.Property(x => x.EmployeeNumber).HasMaxLength(20).IsRequired();
            b.Property(x => x.FirstName).HasMaxLength(80).IsRequired();
            b.Property(x => x.MiddleName).HasMaxLength(80);
            b.Property(x => x.LastName).HasMaxLength(80).IsRequired();
            b.Property(x => x.Gender).HasMaxLength(20).IsRequired();
            b.Property(x => x.Citizenship).HasMaxLength(20).IsRequired();
            b.Property(x => x.ResidencyStatus).HasMaxLength(20).IsRequired();
            b.Property(x => x.PhoneMobile).HasMaxLength(13).IsRequired();
            b.Property(x => x.EmailWork).HasMaxLength(254);
            b.Property(x => x.EmailPersonal).HasMaxLength(254);
            b.Property(x => x.NationalId).HasMaxLength(20);
            b.Property(x => x.PassportNo).HasMaxLength(20);
            b.Property(x => x.WorkPermitNo).HasMaxLength(20);
            b.Property(x => x.KraPin).HasMaxLength(11);
            b.Property(x => x.NssfNo).HasMaxLength(20);
            b.Property(x => x.ShaNo).HasMaxLength(30);
            b.Property(x => x.HelbRef).HasMaxLength(30);
            b.Property(x => x.DisabilityCertNo).HasMaxLength(30);
            b.Property(x => x.PaymentMethod).HasMaxLength(10);
            b.Property(x => x.BankCode).HasMaxLength(10);
            b.Property(x => x.BankBranchCode).HasMaxLength(10);
            b.Property(x => x.AccountNo).HasMaxLength(30);
            b.Property(x => x.AccountName).HasMaxLength(120);
            b.Property(x => x.MpesaPhone).HasMaxLength(13);
            b.Ignore(x => x.FullName);
            b.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.EmployeeNumber }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.NationalId }).IsUnique().HasFilter("national_id is not null").HasDatabaseName("ux_employees_national_id");
            b.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.PassportNo }).IsUnique().HasFilter("passport_no is not null").HasDatabaseName("ux_employees_passport_no");
            b.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.KraPin }).IsUnique().HasFilter("kra_pin is not null").HasDatabaseName("ux_employees_kra_pin");
            b.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.NssfNo }).IsUnique().HasFilter("nssf_no is not null").HasDatabaseName("ux_employees_nssf_no");
            b.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.ShaNo }).IsUnique().HasFilter("sha_no is not null").HasDatabaseName("ux_employees_sha_no");
            b.HasIndex(x => new { x.TenantId, x.LastName, x.FirstName });
        });

        modelBuilder.Entity<EmergencyContact>(b =>
        {
            b.ToTable("emergency_contacts", Schema);
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(120).IsRequired();
            b.Property(x => x.Relationship).HasMaxLength(60).IsRequired();
            b.Property(x => x.Phone).HasMaxLength(16).IsRequired();
            EmployeeFk(b);
            b.HasIndex(x => new { x.TenantId, x.EmployeeId }).HasFilter("is_active");
        });

        modelBuilder.Entity<EmploymentEpisode>(b =>
        {
            b.ToTable("employment_episodes", Schema, t =>
                t.HasCheckConstraint("ck_episodes_dates", "termination_date is null or termination_date >= hire_date"));
            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.TenantId, x.LegalEntityId, x.Id });
            b.Property(x => x.TerminationType).HasMaxLength(30);
            b.Property(x => x.TerminationReason).HasMaxLength(1000);
            EmployeeFk(b);
            b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Sequence }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.EmployeeId }).IsUnique().HasFilter("termination_date is null")
                .HasDatabaseName("ux_employment_episodes_one_open");
        });

        modelBuilder.Entity<AssignmentVersion>(b =>
        {
            b.ToTable("assignment_versions", Schema, t =>
            {
                t.HasCheckConstraint("ck_assignments_category", "worker_category in ('permanent','fixed_term','probation','casual','piece_rate','intern','contractor')");
                t.HasCheckConstraint("ck_assignments_pay_basis", "pay_basis in ('monthly','daily','hourly','piece_rate')");
                t.HasCheckConstraint("ck_assignments_status", "status in ('active','suspended')");
                t.HasCheckConstraint("ck_assignments_not_own_manager", "manager_employee_id is null or manager_employee_id <> employee_id");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.ChangeType).HasMaxLength(20).IsRequired();
            b.Property(x => x.Status).HasMaxLength(20).IsRequired();
            b.Property(x => x.WorkerCategory).HasMaxLength(20).IsRequired();
            b.Property(x => x.PayBasis).HasMaxLength(20).IsRequired();
            b.Property(x => x.JobTitle).HasMaxLength(120);
            b.Property(x => x.Reason).HasMaxLength(1000);
            b.Property(x => x.CancelReason).HasMaxLength(1000);
            EmployeeFk(b);
            b.HasOne<EmploymentEpisode>().WithMany().HasForeignKey(x => new { x.TenantId, x.LegalEntityId, x.EpisodeId })
                .HasPrincipalKey(x => new { x.TenantId, x.LegalEntityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Employee>().WithMany().HasForeignKey(x => new { x.TenantId, x.LegalEntityId, x.ManagerEmployeeId })
                .HasPrincipalKey(x => new { x.TenantId, x.LegalEntityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.EpisodeId, x.EffectiveFrom }).IsUnique().HasFilter("not is_cancelled")
                .HasDatabaseName("ux_assignment_versions_one_per_date");
            b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.EffectiveFrom });
            b.HasIndex(x => new { x.TenantId, x.ManagerEmployeeId });
        });

        modelBuilder.Entity<PaymentDetailsChange>(b =>
        {
            b.ToTable("payment_detail_changes", Schema, t =>
            {
                t.HasCheckConstraint("ck_payment_changes_method", "payment_method in ('bank','mpesa','cash')");
                t.HasCheckConstraint("ck_payment_changes_status", "status in ('pending','approved','rejected','withdrawn')");
                t.HasCheckConstraint("ck_payment_changes_four_eyes", "status <> 'approved' or decided_by_user_id is distinct from requested_by_user_id");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.PaymentMethod).HasMaxLength(10).IsRequired();
            b.Property(x => x.BankCode).HasMaxLength(10);
            b.Property(x => x.BankBranchCode).HasMaxLength(10);
            b.Property(x => x.AccountNo).HasMaxLength(30);
            b.Property(x => x.AccountName).HasMaxLength(120);
            b.Property(x => x.MpesaPhone).HasMaxLength(13);
            b.Property(x => x.Source).HasMaxLength(20).IsRequired();
            b.Property(x => x.Status).HasMaxLength(20).IsRequired();
            b.Property(x => x.Reason).HasMaxLength(1000);
            b.Property(x => x.DecisionNote).HasMaxLength(1000);
            EmployeeFk(b);
            b.HasIndex(x => new { x.TenantId, x.EmployeeId }).IsUnique().HasFilter("status = 'pending'")
                .HasDatabaseName("ux_payment_detail_changes_one_pending");
            b.HasIndex(x => new { x.TenantId, x.Status });
        });

        modelBuilder.Entity<BankBranch>(b =>
        {
            b.ToTable("bank_branches", Schema);
            b.HasKey(x => x.Id);
            b.Property(x => x.BankCode).HasMaxLength(10).IsRequired();
            b.Property(x => x.BankName).HasMaxLength(120).IsRequired();
            b.Property(x => x.BranchCode).HasMaxLength(10).IsRequired();
            b.Property(x => x.BranchName).HasMaxLength(120).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.BankCode, x.BranchCode }).IsUnique();
        });

        modelBuilder.Entity<ImportBatch>(b =>
        {
            b.ToTable("import_batches", Schema, t =>
            {
                t.HasCheckConstraint("ck_import_batches_status", "status in ('validated','committed','discarded')");
                t.HasCheckConstraint("ck_import_batches_four_eyes", "committed_by_user_id is null or committed_by_user_id is distinct from uploaded_by_user_id");
            });
            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.TenantId, x.LegalEntityId, x.Id });
            b.Property(x => x.FileName).HasMaxLength(200).IsRequired();
            b.Property(x => x.FileSha256).HasMaxLength(64).IsRequired();
            b.Property(x => x.Status).HasMaxLength(20).IsRequired();
            b.Property(x => x.UploadedByDisplay).HasMaxLength(200);
            b.HasIndex(x => new { x.TenantId, x.UploadedAt });
        });

        modelBuilder.Entity<ImportRow>(b =>
        {
            b.ToTable("import_rows", Schema);
            b.HasKey(x => x.Id);
            b.Property(x => x.Data).HasColumnType("jsonb");
            b.Property(x => x.Errors).HasColumnType("jsonb");
            b.HasOne<ImportBatch>().WithMany().HasForeignKey(x => new { x.TenantId, x.LegalEntityId, x.BatchId })
                .HasPrincipalKey(x => new { x.TenantId, x.LegalEntityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.BatchId, x.RowNumber }).IsUnique();
        });
    }

    private static void EmployeeFk<T>(EntityTypeBuilder<T> b)
        where T : class, IEmployerScoped
        => b.HasOne<Employee>().WithMany().HasForeignKey(nameof(IEmployerScoped.TenantId), nameof(IEmployerScoped.LegalEntityId), "EmployeeId")
            .HasPrincipalKey(x => new { x.TenantId, x.LegalEntityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
}
