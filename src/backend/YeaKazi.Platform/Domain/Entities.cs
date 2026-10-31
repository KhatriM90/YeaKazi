using YeaKazi.BuildingBlocks.Domain;

namespace YeaKazi.Platform.Domain;

public static class TenantStatuses
{
    public const string Active = "active";
    public const string Suspended = "suspended";
}

/// <summary>Tenant registry row (platform schema). Holds no HR data.</summary>
public sealed class Tenant : IVersioned
{
    public Guid TenantId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = TenantStatuses.Active;
    public string HostingRegion { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? CreatedByPlatformUserId { get; set; }
    public uint Version { get; set; }
}

/// <summary>Idempotency record for SAAS-01 provisioning.</summary>
public sealed class TenantProvisioningRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string ResponseJson { get; set; } = "{}";
    public Guid? CreatedByPlatformUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class PlatformRoles
{
    public const string Admin = "platform_admin";
    public const string Support = "platform_support";
}

/// <summary>Platform operator account (SaaS console). Has no standing access to tenant HR data.</summary>
public sealed class PlatformUser : IVersioned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = PlatformRoles.Support;
    public bool IsActive { get; set; } = true;
    [AuditIgnore] public string? PasswordHash { get; set; }
    [AuditIgnore] public string? MfaSecret { get; set; }
    [AuditIgnore] public string? MfaPendingSecret { get; set; }
    public bool MfaEnabled { get; set; }
    [AuditIgnore] public long MfaLastStep { get; set; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    [AuditIgnore] public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public uint Version { get; set; }
}

public static class UserStatuses
{
    public const string Invited = "invited";
    public const string Active = "active";
    public const string Disabled = "disabled";
}

/// <summary>Tenant user account (identity, not the employee record).</summary>
public sealed class User : ITenantOwned, IVersioned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    [AuditMask] public string Email { get; set; } = string.Empty;
    [AuditIgnore] public string NormalizedEmail { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = UserStatuses.Invited;
    [AuditIgnore] public string? PasswordHash { get; set; }
    [AuditIgnore] public string? MfaSecret { get; set; }
    [AuditIgnore] public string? MfaPendingSecret { get; set; }
    public bool MfaEnabled { get; set; }
    [AuditIgnore] public long MfaLastStep { get; set; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    [AuditIgnore] public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public uint Version { get; set; }
}

public sealed class UserInvitation : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    [AuditIgnore] public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Role : ITenantOwned, IVersioned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public uint Version { get; set; }
}

public sealed class RolePermission : ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid RoleId { get; set; }
    public string Permission { get; set; } = string.Empty;
}

/// <summary>Role held by a user, for all employers (LegalEntityId null) or one employer.</summary>
public sealed class UserRoleAssignment : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public Guid? LegalEntityId { get; set; }
    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? AssignedByUserId { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedByUserId { get; set; }
}
