using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using YeaKazi.BuildingBlocks.Audit;
using YeaKazi.BuildingBlocks.Errors;
using YeaKazi.BuildingBlocks.Security;
using YeaKazi.Platform.Domain;
using YeaKazi.Platform.Persistence;

namespace YeaKazi.Platform.Users;

public sealed record RoleDto(Guid Id, string Code, string Name, string? Description, bool IsSystem, uint Version, IReadOnlyList<string> Permissions);

public sealed record PermissionDto(string Code, string Description, bool RequiresMfa);

public sealed record CreateRoleRequest(string Code, string Name, string? Description, IReadOnlyList<string> Permissions);

public sealed record UpdateRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions, uint Version, string Reason);

/// <summary>System roles (Section 6) are seeded and read-only; tenants may add custom roles from the catalogue.</summary>
public sealed partial class RoleService(PlatformDbContext db, IAccessControl access, AuditTrail audit)
{
    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct)
    {
        await access.RequireAsync(Permissions.UsersRead, null, ct);
        var roles = await db.Roles.AsNoTracking().OrderByDescending(r => r.IsSystem).ThenBy(r => r.Name).ToListAsync(ct);
        var perms = await db.RolePermissions.AsNoTracking().ToListAsync(ct);
        return roles.Select(r => ToDto(r, perms.Where(p => p.RoleId == r.Id).Select(p => p.Permission))).ToList();
    }

    public static IReadOnlyList<PermissionDto> Catalogue()
        => Permissions.Catalogue.Select(kv => new PermissionDto(kv.Key, kv.Value, Permissions.Privileged.Contains(kv.Key))).ToList();

    public async Task<RoleDto> CreateAsync(CreateRoleRequest request, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.RolesManage, null, ct);
        Validate(request.Code, request.Name, request.Description, request.Permissions, validateCode: true);
        var code = request.Code.Trim().ToLowerInvariant();
        if (SystemRoles.All.Any(r => r.Code == code) || await db.Roles.AnyAsync(r => r.Code == code, ct))
        {
            throw new ConflictException("duplicate_role", "A role with this code already exists.");
        }

        var role = new Role { Code = code, Name = request.Name.Trim(), Description = request.Description?.Trim(), IsSystem = false };
        db.Roles.Add(role);
        var permissions = request.Permissions.Distinct().Order().ToList();
        foreach (var p in permissions)
        {
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, Permission = p });
        }

        audit.Record(db, "role.created", "role", role.Id, after: AuditSnapshot.Of(new { role.Code, role.Name, role.Description, permissions }));
        await db.SaveChangesAsync(ct);
        return ToDto(role, permissions);
    }

    public async Task<RoleDto> UpdateAsync(Guid id, UpdateRoleRequest request, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.RolesManage, null, ct);
        Validate("x", request.Name, request.Description, request.Permissions, validateCode: false);
        new Validator().Required("reason", request.Reason, 1000).ThrowIfInvalid();
        var role = await db.Roles.SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Role");
        if (role.IsSystem)
        {
            throw new BusinessRuleException("system_role_readonly", "System roles cannot be changed. Create a custom role instead.");
        }

        if (role.Version != request.Version)
        {
            throw new ConflictException("concurrency_conflict", "The role was changed by someone else. Reload and try again.");
        }

        var existing = await db.RolePermissions.Where(p => p.RoleId == id).ToListAsync(ct);
        var before = AuditSnapshot.Of(new { role.Name, role.Description, permissions = existing.Select(p => p.Permission).Order().ToList() });
        var target = request.Permissions.Distinct().ToHashSet();
        db.RolePermissions.RemoveRange(existing.Where(p => !target.Contains(p.Permission)));
        foreach (var p in target.Where(p => existing.All(e => e.Permission != p)))
        {
            db.RolePermissions.Add(new RolePermission { RoleId = id, Permission = p });
        }

        role.Name = request.Name.Trim();
        role.Description = request.Description?.Trim();
        db.Entry(role).Property(r => r.Version).OriginalValue = request.Version;
        // Touch the row so a permission-only change still bumps the concurrency version.
        db.Entry(role).Property(r => r.Name).IsModified = true;
        var after = target.Order().ToList();
        audit.Record(db, "role.updated", "role", role.Id, before, AuditSnapshot.Of(new { role.Name, role.Description, permissions = after }), request.Reason);
        await db.SaveChangesAsync(ct);
        return ToDto(role, after);
    }

    private static void Validate(string code, string name, string? description, IReadOnlyList<string>? permissions, bool validateCode)
    {
        var v = new Validator().Required("name", name, 120).Optional("description", description, 400);
        if (validateCode)
        {
            v.Required("code", code, 60).Must(code is null || CodePattern().IsMatch(code.Trim().ToLowerInvariant()), "code", "Use lowercase letters, digits and underscores.");
        }

        v.Must(permissions is { Count: > 0 }, "permissions", "Choose at least one permission.");
        foreach (var p in permissions ?? [])
        {
            v.Must(Permissions.Catalogue.ContainsKey(p), "permissions", $"Unknown permission '{p}'.");
        }

        v.ThrowIfInvalid();
    }

    private static RoleDto ToDto(Role r, IEnumerable<string> permissions)
        => new(r.Id, r.Code, r.Name, r.Description, r.IsSystem, r.Version, permissions.Order().ToList());

    [GeneratedRegex("^[a-z][a-z0-9_]{1,59}$")]
    private static partial Regex CodePattern();
}
