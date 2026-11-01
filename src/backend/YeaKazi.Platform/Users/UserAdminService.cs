using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using YeaKazi.BuildingBlocks.Audit;
using YeaKazi.BuildingBlocks.Contracts;
using YeaKazi.BuildingBlocks.Errors;
using YeaKazi.BuildingBlocks.Http;
using YeaKazi.BuildingBlocks.Security;
using YeaKazi.BuildingBlocks.Tenancy;
using YeaKazi.Platform.Access;
using YeaKazi.Platform.Auth;
using YeaKazi.Platform.Domain;
using YeaKazi.Platform.Persistence;

namespace YeaKazi.Platform.Users;

public sealed record RoleAssignmentDto(Guid Id, Guid RoleId, string RoleCode, string RoleName, Guid? LegalEntityId, DateTimeOffset AssignedAt);

public sealed record UserDto(Guid Id, string Email, string DisplayName, string Status, bool MfaEnabled, DateTimeOffset? LastLoginAt, uint Version, IReadOnlyList<RoleAssignmentDto> Roles);

public sealed record InviteUserRequest(string Email, string DisplayName, IReadOnlyList<RoleGrant>? Roles);

public sealed record RoleGrant(Guid RoleId, Guid? LegalEntityId);

public sealed record DisableUserRequest(uint Version, string Reason);

public sealed record RevokeRoleRequest(string Reason);

public sealed record MeDto(Guid UserId, string DisplayName, string Email, Guid TenantId, string TenantName, string TenantSlug, bool MfaEnabled, IReadOnlyList<string> Permissions, IReadOnlyList<RoleAssignmentDto> Roles);

public sealed record InvitationMessage(Guid UserId, string Email, string DisplayName, string TenantSlug, string TenantName, string EncryptedToken, DateTimeOffset ExpiresAt);

public static class OutboxTypes
{
    public const string UserInvited = "iam.user_invited";
}

/// <summary>Tenant user administration with separation-of-duties rules.</summary>
public sealed class UserAdminService(
    PlatformDbContext db,
    RequestContext context,
    IAccessControl access,
    IOrganizationDirectory organization,
    IFieldEncryptor encryptor,
    AuditTrail audit,
    IOptions<AuthOptions> options,
    TimeProvider clock)
{
    public async Task<PagedResult<UserDto>> ListAsync(string? search, int? page, int? pageSize, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.UsersRead, null, ct);
        var (p, s) = Paging.Normalize(page, pageSize);
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(u => u.NormalizedEmail.Contains(term) || u.DisplayName.ToUpper().Contains(term));
        }

        var total = await query.CountAsync(ct);
        var users = await query.OrderBy(u => u.DisplayName).Skip((p - 1) * s).Take(s).ToListAsync(ct);
        var roles = await LoadAssignmentsAsync(users.Select(u => u.Id).ToList(), ct);
        return new PagedResult<UserDto>(users.Select(u => ToDto(u, roles)).ToList(), p, s, total);
    }

    public async Task<UserDto> GetAsync(Guid id, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.UsersRead, null, ct);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("User");
        var roles = await LoadAssignmentsAsync([id], ct);
        return ToDto(user, roles);
    }

    public async Task<MeDto> MeAsync(CancellationToken ct)
    {
        var userId = context.ActorUserId ?? throw new ForbiddenException();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, ct);
        var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.TenantId == context.TenantId, ct);
        var permissions = await AccessControl.LoadForUserAsync(db, userId, ct);
        var roles = await LoadAssignmentsAsync([userId], ct);
        return new MeDto(user.Id, user.DisplayName, user.Email, tenant.TenantId, tenant.Name, tenant.Slug, user.MfaEnabled,
            permissions.Keys.Order().ToList(), roles.GetValueOrDefault(userId) ?? []);
    }

    public async Task<UserDto> InviteAsync(InviteUserRequest request, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.UsersManage, null, ct);
        new Validator()
            .Required("email", request.Email, 254)
            .Must(request.Email is null || IsEmail(request.Email), "email", "Enter a valid email address.")
            .Required("displayName", request.DisplayName, 200)
            .ThrowIfInvalid();

        var normalized = TenantAuthService.Normalize(request.Email!);
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct))
        {
            throw new ConflictException("duplicate_email", "A user with this email already exists.");
        }

        var user = new User
        {
            Email = request.Email!.Trim(),
            NormalizedEmail = normalized,
            DisplayName = request.DisplayName!.Trim(),
            Status = UserStatuses.Invited,
        };
        db.Users.Add(user);
        audit.Record(db, "user.invited", "user", user.Id, after: AuditSnapshot.Of(user));

        foreach (var grant in request.Roles ?? [])
        {
            await AddAssignmentAsync(user.Id, grant, ct);
        }

        await IssueInvitationAsync(user, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(user.Id, ct);
    }

    /// <summary>Creates an invitation token; only its hash is stored, and the token travels encrypted via the outbox.</summary>
    internal async Task IssueInvitationAsync(User user, CancellationToken ct)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expires = clock.GetUtcNow().AddHours(options.Value.InvitationHours);
        db.UserInvitations.Add(new UserInvitation
        {
            UserId = user.Id,
            TokenHash = TenantAuthService.HashToken(token),
            ExpiresAt = expires,
        });
        var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.TenantId == context.TenantId, ct);
        audit.Enqueue(db, OutboxTypes.UserInvited, new InvitationMessage(
            user.Id, user.Email, user.DisplayName, tenant.Slug, tenant.Name, encryptor.Encrypt(token), expires));
    }

    public async Task<UserDto> AssignRoleAsync(Guid userId, RoleGrant grant, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.UsersManage, grant.LegalEntityId, ct);
        EnsureNotSelf(userId);
        _ = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException("User");
        await AddAssignmentAsync(userId, grant, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(userId, ct);
    }

    public async Task<UserDto> RevokeRoleAsync(Guid userId, Guid assignmentId, RevokeRoleRequest request, CancellationToken ct)
    {
        new Validator().Required("reason", request.Reason, 1000).ThrowIfInvalid();
        var assignment = await db.UserRoleAssignments.SingleOrDefaultAsync(a => a.Id == assignmentId && a.UserId == userId && a.RevokedAt == null, ct)
            ?? throw new NotFoundException("Role assignment");
        await access.RequireAsync(Permissions.UsersManage, assignment.LegalEntityId, ct);
        EnsureNotSelf(userId);

        var role = await db.Roles.AsNoTracking().SingleAsync(r => r.Id == assignment.RoleId, ct);
        if (role.Code == SystemRoles.TenantAdministrator && assignment.LegalEntityId is null)
        {
            await EnsureAnotherActiveAdministratorAsync(userId, ct);
        }

        var before = AuditSnapshot.Of(assignment);
        assignment.RevokedAt = clock.GetUtcNow();
        assignment.RevokedByUserId = context.ActorUserId;
        audit.Record(db, "user.role_revoked", "user_role_assignment", assignment.Id, before, AuditSnapshot.Of(assignment), request.Reason, assignment.LegalEntityId);
        await db.SaveChangesAsync(ct);
        return await GetAsync(userId, ct);
    }

    public async Task<UserDto> DisableAsync(Guid userId, DisableUserRequest request, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.UsersManage, null, ct);
        new Validator().Required("reason", request.Reason, 1000).ThrowIfInvalid();
        EnsureNotSelf(userId);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException("User");
        if (user.Version != request.Version)
        {
            throw new ConflictException("concurrency_conflict", "The user was changed by someone else. Reload and try again.");
        }

        if (user.Status == UserStatuses.Disabled)
        {
            return await GetAsync(userId, ct);
        }

        await EnsureAnotherActiveAdministratorAsync(userId, ct);
        var before = AuditSnapshot.Of(user);
        user.Status = UserStatuses.Disabled;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        db.Entry(user).Property(u => u.Version).OriginalValue = request.Version;
        audit.Record(db, "user.disabled", "user", user.Id, before, AuditSnapshot.Of(user), request.Reason);
        await db.SaveChangesAsync(ct);
        return await GetAsync(userId, ct);
    }

    private async Task AddAssignmentAsync(Guid userId, RoleGrant grant, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.UsersManage, grant.LegalEntityId, ct);
        var role = await db.Roles.AsNoTracking().SingleOrDefaultAsync(r => r.Id == grant.RoleId, ct)
            ?? throw new ValidationException(new Dictionary<string, string[]> { ["roleId"] = ["Unknown role."] });
        if (grant.LegalEntityId is { } le && !await organization.LegalEntityExistsAsync(le, ct))
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["legalEntityId"] = ["Unknown legal employer."] });
        }

        var exists = await db.UserRoleAssignments.AnyAsync(a => a.UserId == userId && a.RoleId == grant.RoleId
            && a.LegalEntityId == grant.LegalEntityId && a.RevokedAt == null, ct);
        if (exists)
        {
            throw new ConflictException("duplicate_assignment", "The user already holds this role for this scope.");
        }

        var assignment = new UserRoleAssignment
        {
            UserId = userId,
            RoleId = role.Id,
            LegalEntityId = grant.LegalEntityId,
            AssignedAt = clock.GetUtcNow(),
            AssignedByUserId = context.ActorUserId,
        };
        db.UserRoleAssignments.Add(assignment);
        audit.Record(db, "user.role_assigned", "user_role_assignment", assignment.Id,
            after: AuditSnapshot.Of(new { assignment.UserId, assignment.RoleId, role.Code, assignment.LegalEntityId }),
            legalEntityId: grant.LegalEntityId);
    }

    /// <summary>Users cannot change their own access (separation of duties).</summary>
    private void EnsureNotSelf(Guid userId)
    {
        if (context.ActorUserId == userId)
        {
            throw new BusinessRuleException("self_administration", "You cannot change your own access. Ask another administrator.");
        }
    }

    private async Task EnsureAnotherActiveAdministratorAsync(Guid userId, CancellationToken ct)
    {
        var others = await (
            from a in db.UserRoleAssignments
            join r in db.Roles on a.RoleId equals r.Id
            join u in db.Users on a.UserId equals u.Id
            where r.Code == SystemRoles.TenantAdministrator && a.RevokedAt == null && a.LegalEntityId == null
                  && u.Status == UserStatuses.Active && u.Id != userId
            select a.Id).AnyAsync(ct);
        var isAdmin = await (
            from a in db.UserRoleAssignments
            join r in db.Roles on a.RoleId equals r.Id
            where r.Code == SystemRoles.TenantAdministrator && a.RevokedAt == null && a.UserId == userId
            select a.Id).AnyAsync(ct);
        if (isAdmin && !others)
        {
            throw new BusinessRuleException("last_administrator", "The tenant must keep at least one active tenant administrator.");
        }
    }

    private async Task<Dictionary<Guid, List<RoleAssignmentDto>>> LoadAssignmentsAsync(List<Guid> userIds, CancellationToken ct)
    {
        var rows = await (
            from a in db.UserRoleAssignments.AsNoTracking()
            join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
            where userIds.Contains(a.UserId) && a.RevokedAt == null
            orderby r.Name
            select new { a.UserId, Dto = new RoleAssignmentDto(a.Id, r.Id, r.Code, r.Name, a.LegalEntityId, a.AssignedAt) })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => g.Select(x => x.Dto).ToList());
    }

    private static UserDto ToDto(User u, Dictionary<Guid, List<RoleAssignmentDto>> roles)
        => new(u.Id, u.Email, u.DisplayName, u.Status, u.MfaEnabled, u.LastLoginAt, u.Version, roles.GetValueOrDefault(u.Id) ?? []);

    internal static bool IsEmail(string value)
    {
        var v = value.Trim();
        var at = v.IndexOf('@');
        return at > 0 && at == v.LastIndexOf('@') && v.IndexOf('.', at) > at + 1 && !v.EndsWith('.') && !v.Contains(' ');
    }
}
