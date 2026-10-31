using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using YeaKazi.BuildingBlocks.Audit;
using YeaKazi.BuildingBlocks.Contracts;
using YeaKazi.BuildingBlocks.Errors;
using YeaKazi.BuildingBlocks.Http;
using YeaKazi.BuildingBlocks.Persistence;
using YeaKazi.BuildingBlocks.Security;
using YeaKazi.BuildingBlocks.Tenancy;
using YeaKazi.Platform.Auth;
using YeaKazi.Platform.Domain;
using YeaKazi.Platform.Persistence;
using YeaKazi.Platform.Users;

namespace YeaKazi.Platform.Provisioning;

public sealed record ProvisionTenantRequest(
    string Name,
    string Slug,
    string HostingRegion,
    ProvisionLegalEntity LegalEntity,
    ProvisionAdministrator Administrator);

public sealed record ProvisionLegalEntity(string Code, string Name, string? RegistrationNumber, string? KraPin, string? NssfEmployerNumber, string? Timezone);

public sealed record ProvisionAdministrator(string Email, string DisplayName);

public sealed record ProvisionTenantResponse(Guid TenantId, string Slug, string Status, Guid LegalEntityId, Guid AdministratorUserId, IReadOnlyList<string> SeededRoles);

public sealed record ProvisionOutcome(ProvisionTenantResponse Response, bool Replayed);

public sealed record TenantSummaryDto(Guid TenantId, string Slug, string Name, string Status, string HostingRegion, DateTimeOffset CreatedAt);

public sealed class PlatformOptions
{
    public const string Section = "Platform";

    /// <summary>Hosting regions the operator offers (SAAS-01). Values are operator configuration, not product constants.</summary>
    public List<string> HostingRegions { get; set; } = [];
}

/// <summary>
/// SAAS-01: creates a tenant, its first legal employer, the Section 6 system roles and
/// the first tenant administrator in one transaction. Idempotent by Idempotency-Key;
/// the same key with a different request is rejected.
/// </summary>
public sealed partial class TenantProvisioningService(
    PlatformDbContext db,
    SharedDbConnection shared,
    RequestContext context,
    IOrganizationProvisioning organization,
    UserAdminService users,
    AuditTrail audit,
    IOptions<PlatformOptions> platformOptions)
{
    private static readonly JsonSerializerOptions HashOptions = new(JsonSerializerDefaults.Web);

    public async Task<PagedResult<TenantSummaryDto>> ListAsync(string? search, int? page, int? pageSize, CancellationToken ct)
    {
        RequirePlatform(allowSupport: true);
        var (p, s) = Paging.Normalize(page, pageSize);
        var q = db.Tenants.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            q = q.Where(t => t.Slug.Contains(term) || t.Name.ToLower().Contains(term));
        }

        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(t => t.Name).Skip((p - 1) * s).Take(s)
            .Select(t => new TenantSummaryDto(t.TenantId, t.Slug, t.Name, t.Status, t.HostingRegion, t.CreatedAt))
            .ToListAsync(ct);
        return new PagedResult<TenantSummaryDto>(rows, p, s, total);
    }

    public IReadOnlyList<string> HostingRegions()
    {
        RequirePlatform(allowSupport: true);
        return platformOptions.Value.HostingRegions;
    }

    public async Task<ProvisionOutcome> ProvisionAsync(string? idempotencyKey, ProvisionTenantRequest request, CancellationToken ct)
    {
        RequirePlatform(allowSupport: false);
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length is < 8 or > 100)
        {
            throw new BusinessRuleException("idempotency_key_required", "An Idempotency-Key header of 8 to 100 characters is required.");
        }

        var normalized = Normalize(request);
        Validate(normalized);
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(normalized, HashOptions)))).ToLowerInvariant();

        await using var tx = await shared.BeginTransactionAsync(ct: ct);
        db.EnlistInSharedTransaction();
        await db.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtextextended({"provision:" + idempotencyKey}, 0))", ct);

        var existing = await db.ProvisioningRequests.AsNoTracking().SingleOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, ct);
        if (existing is not null)
        {
            if (existing.RequestHash != requestHash)
            {
                throw new BusinessRuleException("idempotency_key_reused", "This Idempotency-Key was already used for a different request.");
            }

            return new ProvisionOutcome(JsonSerializer.Deserialize<ProvisionTenantResponse>(existing.ResponseJson, HashOptions)!, true);
        }

        if (await db.Tenants.AnyAsync(t => t.Slug == normalized.Slug, ct))
        {
            throw new ConflictException("duplicate_slug", "This tenant address is already taken.");
        }

        var tenant = new Tenant
        {
            TenantId = Guid.CreateVersion7(),
            Slug = normalized.Slug,
            Name = normalized.Name,
            HostingRegion = normalized.HostingRegion,
            Status = TenantStatuses.Active,
            CreatedByPlatformUserId = context.ActorUserId,
        };
        db.Tenants.Add(tenant);
        audit.RecordPlatform(db, "platform.tenant.created", "tenant", tenant.TenantId, tenant.TenantId,
            AuditSnapshot.Of(new { tenant.Slug, tenant.Name, tenant.HostingRegion }));
        await db.SaveChangesAsync(ct);

        // Seed the new tenant's records under its own tenant id (RLS WITH CHECK applies).
        context.BeginProvisioning(tenant.TenantId);
        await shared.ApplySessionAsync(ct);

        audit.Record(db, "tenant.provisioned", "tenant", tenant.TenantId,
            after: AuditSnapshot.Of(new { tenant.Slug, tenant.Name, tenant.HostingRegion, provisionedBy = context.ActorDisplay }));

        var seeded = new List<string>();
        Guid adminRoleId = Guid.Empty;
        foreach (var def in SystemRoles.All)
        {
            var role = new Role { Code = def.Code, Name = def.Name, Description = def.Description, IsSystem = true };
            db.Roles.Add(role);
            foreach (var p in def.Permissions)
            {
                db.RolePermissions.Add(new RolePermission { RoleId = role.Id, Permission = p });
            }

            if (def.Code == SystemRoles.TenantAdministrator)
            {
                adminRoleId = role.Id;
            }

            seeded.Add(def.Code);
        }

        audit.Record(db, "role.system_roles_seeded", "role", null,
            after: AuditSnapshot.Of(SystemRoles.All.Select(r => new { r.Code, r.Permissions })));
        await db.SaveChangesAsync(ct);

        var legalEntityId = await organization.CreateInitialLegalEntityAsync(new InitialLegalEntity(
            normalized.LegalEntity.Code,
            normalized.LegalEntity.Name,
            normalized.LegalEntity.RegistrationNumber,
            normalized.LegalEntity.KraPin,
            normalized.LegalEntity.NssfEmployerNumber,
            normalized.LegalEntity.Timezone ?? "Africa/Nairobi"), ct);

        var admin = new User
        {
            Email = normalized.Administrator.Email,
            NormalizedEmail = TenantAuthService.Normalize(normalized.Administrator.Email),
            DisplayName = normalized.Administrator.DisplayName,
            Status = UserStatuses.Invited,
        };
        db.Users.Add(admin);
        audit.Record(db, "user.invited", "user", admin.Id, after: AuditSnapshot.Of(admin));
        var assignment = new UserRoleAssignment { UserId = admin.Id, RoleId = adminRoleId, LegalEntityId = null };
        db.UserRoleAssignments.Add(assignment);
        audit.Record(db, "user.role_assigned", "user_role_assignment", assignment.Id,
            after: AuditSnapshot.Of(new { assignment.UserId, assignment.RoleId, Code = SystemRoles.TenantAdministrator, assignment.LegalEntityId }));
        await users.IssueInvitationAsync(admin, ct);
        await db.SaveChangesAsync(ct);

        context.EndProvisioning();
        await shared.ApplySessionAsync(ct);

        var response = new ProvisionTenantResponse(tenant.TenantId, tenant.Slug, tenant.Status, legalEntityId, admin.Id, seeded);
        db.ProvisioningRequests.Add(new TenantProvisioningRequest
        {
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            TenantId = tenant.TenantId,
            ResponseJson = JsonSerializer.Serialize(response, HashOptions),
            CreatedByPlatformUserId = context.ActorUserId,
        });
        audit.RecordPlatform(db, "platform.tenant.provisioned", "tenant", tenant.TenantId, tenant.TenantId,
            AuditSnapshot.Of(new { legalEntityId, administratorUserId = admin.Id, seededRoles = seeded }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new ProvisionOutcome(response, false);
    }

    private void RequirePlatform(bool allowSupport)
    {
        if (!context.IsPlatformScope || context.ActorKind != ActorKinds.PlatformUser)
        {
            throw new ForbiddenException();
        }

        if (!allowSupport && PlatformRole != PlatformRoles.Admin)
        {
            throw new ForbiddenException("Only platform administrators can provision tenants.");
        }
    }

    /// <summary>Set by the endpoint from the validated token.</summary>
    public string? PlatformRole { get; set; }

    private static ProvisionTenantRequest Normalize(ProvisionTenantRequest r) => new(
        r.Name?.Trim() ?? string.Empty,
        r.Slug?.Trim().ToLowerInvariant() ?? string.Empty,
        r.HostingRegion?.Trim() ?? string.Empty,
        new ProvisionLegalEntity(
            r.LegalEntity?.Code?.Trim().ToUpperInvariant() ?? string.Empty,
            r.LegalEntity?.Name?.Trim() ?? string.Empty,
            NullIfEmpty(r.LegalEntity?.RegistrationNumber),
            NullIfEmpty(r.LegalEntity?.KraPin)?.ToUpperInvariant(),
            NullIfEmpty(r.LegalEntity?.NssfEmployerNumber),
            NullIfEmpty(r.LegalEntity?.Timezone)),
        new ProvisionAdministrator(
            r.Administrator?.Email?.Trim() ?? string.Empty,
            r.Administrator?.DisplayName?.Trim() ?? string.Empty));

    private static string? NullIfEmpty(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private void Validate(ProvisionTenantRequest r)
    {
        var v = new Validator()
            .Required("name", r.Name, 200)
            .Required("slug", r.Slug, 40)
            .Must(SlugPattern().IsMatch(r.Slug), "slug", "Use 3 to 40 lowercase letters, digits or hyphens.")
            .Must(platformOptions.Value.HostingRegions.Contains(r.HostingRegion), "hostingRegion", "Choose a configured hosting region.")
            .Required("legalEntity.code", r.LegalEntity.Code, 20)
            .Required("legalEntity.name", r.LegalEntity.Name, 200)
            .Optional("legalEntity.registrationNumber", r.LegalEntity.RegistrationNumber, 50)
            .Optional("legalEntity.nssfEmployerNumber", r.LegalEntity.NssfEmployerNumber, 20)
            .Required("administrator.email", r.Administrator.Email, 254)
            .Must(UserAdminService.IsEmail(r.Administrator.Email), "administrator.email", "Enter a valid email address.")
            .Required("administrator.displayName", r.Administrator.DisplayName, 200);
        v.ThrowIfInvalid();
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,38}[a-z0-9]$")]
    private static partial Regex SlugPattern();
}
