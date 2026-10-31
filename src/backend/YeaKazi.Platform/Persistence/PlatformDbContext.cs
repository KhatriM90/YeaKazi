using Microsoft.EntityFrameworkCore;
using YeaKazi.BuildingBlocks.Audit;
using YeaKazi.BuildingBlocks.Persistence;
using YeaKazi.BuildingBlocks.Tenancy;
using YeaKazi.Platform.Domain;

namespace YeaKazi.Platform.Persistence;

/// <summary>
/// Platform module: tenant registry and platform operators (schema "platform"),
/// tenant identity and roles (schema "iam"), plus the shared audit and outbox tables.
/// </summary>
public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options, RequestContext requestContext, SharedDbConnection? shared = null)
    : ModuleDbContext(options, requestContext, shared)
{
    public const string PlatformSchema = "platform";
    public const string IamSchema = "iam";

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantProvisioningRequest> ProvisioningRequests => Set<TenantProvisioningRequest>();
    public DbSet<PlatformUser> PlatformUsers => Set<PlatformUser>();
    public DbSet<PlatformAuditEvent> PlatformAuditEvents => Set<PlatformAuditEvent>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserInvitation> UserInvitations => Set<UserInvitation>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRoleAssignment> UserRoleAssignments => Set<UserRoleAssignment>();

    protected override bool OwnsSharedTables => true;

    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(b =>
        {
            b.ToTable("tenants", PlatformSchema);
            b.HasKey(x => x.TenantId);
            b.Property(x => x.Slug).HasMaxLength(40).IsRequired();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Status).HasMaxLength(20).IsRequired();
            b.Property(x => x.HostingRegion).HasMaxLength(40).IsRequired();
            b.HasIndex(x => x.Slug).IsUnique();
        });

        modelBuilder.Entity<TenantProvisioningRequest>(b =>
        {
            b.ToTable("tenant_provisioning_requests", PlatformSchema);
            b.HasKey(x => x.IdempotencyKey);
            b.Property(x => x.IdempotencyKey).HasMaxLength(100);
            b.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
            b.Property(x => x.ResponseJson).HasColumnType("jsonb").IsRequired();
            b.HasIndex(x => x.TenantId).IsUnique();
            b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PlatformUser>(b =>
        {
            b.ToTable("platform_users", PlatformSchema);
            b.HasKey(x => x.Id);
            b.Property(x => x.Email).HasMaxLength(254).IsRequired();
            b.Property(x => x.NormalizedEmail).HasMaxLength(254).IsRequired();
            b.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            b.Property(x => x.Role).HasMaxLength(40).IsRequired();
            b.Property(x => x.PasswordHash).HasMaxLength(400);
            b.Property(x => x.MfaSecret).HasMaxLength(400);
            b.Property(x => x.MfaPendingSecret).HasMaxLength(400);
            b.Property(x => x.SecurityStamp).HasMaxLength(64).IsRequired();
            b.HasIndex(x => x.NormalizedEmail).IsUnique();
        });

        modelBuilder.Entity<PlatformAuditEvent>(b =>
        {
            b.ToTable("platform_audit_events", PlatformSchema);
            b.HasKey(x => x.Id);
            b.Property(x => x.ActorKind).HasMaxLength(20).IsRequired();
            b.Property(x => x.ActorDisplay).HasMaxLength(200);
            b.Property(x => x.Action).HasMaxLength(100).IsRequired();
            b.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
            b.Property(x => x.EntityId).HasMaxLength(100);
            b.Property(x => x.Details).HasColumnType("jsonb");
            b.Property(x => x.Reason).HasMaxLength(1000);
            b.Property(x => x.CorrelationId).HasMaxLength(64);
            b.Property(x => x.ClientIp).HasMaxLength(64);
            b.Property(x => x.Seq).ValueGeneratedOnAdd().HasDefaultValue(0L);
            b.Property(x => x.PrevHash).HasMaxLength(64).ValueGeneratedOnAdd().HasDefaultValue(string.Empty);
            b.Property(x => x.Hash).HasMaxLength(64).ValueGeneratedOnAdd().HasDefaultValue(string.Empty);
            b.HasIndex(x => x.Seq).IsUnique();
            b.HasIndex(x => x.TargetTenantId);
        });

        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("users", IamSchema);
            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.TenantId, x.Id });
            b.Property(x => x.Email).HasMaxLength(254).IsRequired();
            b.Property(x => x.NormalizedEmail).HasMaxLength(254).IsRequired();
            b.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            b.Property(x => x.Status).HasMaxLength(20).IsRequired();
            b.Property(x => x.PasswordHash).HasMaxLength(400);
            b.Property(x => x.MfaSecret).HasMaxLength(400);
            b.Property(x => x.MfaPendingSecret).HasMaxLength(400);
            b.Property(x => x.SecurityStamp).HasMaxLength(64).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.NormalizedEmail }).IsUnique();
            b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserInvitation>(b =>
        {
            b.ToTable("user_invitations", IamSchema);
            b.HasKey(x => x.Id);
            b.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.TokenHash }).IsUnique();
            b.HasOne<User>().WithMany().HasForeignKey(x => new { x.TenantId, x.UserId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Role>(b =>
        {
            b.ToTable("roles", IamSchema);
            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.TenantId, x.Id });
            b.Property(x => x.Code).HasMaxLength(60).IsRequired();
            b.Property(x => x.Name).HasMaxLength(120).IsRequired();
            b.Property(x => x.Description).HasMaxLength(400);
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RolePermission>(b =>
        {
            b.ToTable("role_permissions", IamSchema);
            b.HasKey(x => new { x.TenantId, x.RoleId, x.Permission });
            b.Property(x => x.Permission).HasMaxLength(80);
            b.HasOne<Role>().WithMany().HasForeignKey(x => new { x.TenantId, x.RoleId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserRoleAssignment>(b =>
        {
            b.ToTable("user_role_assignments", IamSchema);
            b.HasKey(x => x.Id);
            b.HasOne<User>().WithMany().HasForeignKey(x => new { x.TenantId, x.UserId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Role>().WithMany().HasForeignKey(x => new { x.TenantId, x.RoleId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.UserId, x.RoleId, x.LegalEntityId })
                .IsUnique()
                .AreNullsDistinct(false)
                .HasFilter("revoked_at is null")
                .HasDatabaseName("ux_user_role_assignments_active");
        });
    }
}
