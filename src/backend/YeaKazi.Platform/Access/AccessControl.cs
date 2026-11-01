using Microsoft.EntityFrameworkCore;
using YeaKazi.BuildingBlocks.Errors;
using YeaKazi.BuildingBlocks.Security;
using YeaKazi.BuildingBlocks.Tenancy;
using YeaKazi.Platform.Persistence;

namespace YeaKazi.Platform.Access;

/// <summary>
/// Evaluates the current tenant user's active role assignments on every request
/// (no permissions are cached in the token, so revocation is immediate).
/// </summary>
public sealed class AccessControl(PlatformDbContext db, RequestContext context) : IAccessControl
{
    private Dictionary<string, PermissionScope>? _cache;

    public async Task RequireAsync(string permission, Guid? legalEntityId = null, CancellationToken ct = default)
    {
        var scope = await ScopeAsync(permission, ct);
        var allowed = legalEntityId is null ? scope.Granted : scope.Allows(legalEntityId.Value);
        if (!allowed)
        {
            throw new ForbiddenException();
        }
    }

    public async Task<PermissionScope> ScopeAsync(string permission, CancellationToken ct = default)
    {
        var all = await LoadAsync(ct);
        return all.TryGetValue(permission, out var scope) ? scope : PermissionScope.None;
    }

    public async Task<IReadOnlyDictionary<string, PermissionScope>> AllAsync(CancellationToken ct = default)
        => await LoadAsync(ct);

    private async Task<Dictionary<string, PermissionScope>> LoadAsync(CancellationToken ct)
    {
        if (_cache is not null)
        {
            return _cache;
        }

        if (context.ActorKind != ActorKinds.TenantUser || context.ActorUserId is null || context.TenantId is null)
        {
            _cache = new Dictionary<string, PermissionScope>();
            return _cache;
        }

        _cache = await LoadForUserAsync(db, context.ActorUserId.Value, ct);
        return _cache;
    }

    internal static async Task<Dictionary<string, PermissionScope>> LoadForUserAsync(PlatformDbContext db, Guid userId, CancellationToken ct)
    {
        var rows = await (
            from a in db.UserRoleAssignments
            join rp in db.RolePermissions on a.RoleId equals rp.RoleId
            where a.UserId == userId && a.RevokedAt == null
            select new { rp.Permission, a.LegalEntityId })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.Permission)
            .ToDictionary(
                g => g.Key,
                g => g.Any(r => r.LegalEntityId is null)
                    ? new PermissionScope(true, new HashSet<Guid>())
                    : new PermissionScope(false, g.Select(r => r.LegalEntityId!.Value).ToHashSet()));
    }

    public static bool RequiresMfa(IEnumerable<string> permissions)
        => permissions.Any(Permissions.Privileged.Contains);
}
