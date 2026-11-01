using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using YeaKazi.BuildingBlocks.Http;
using YeaKazi.Platform.AuditLog;
using YeaKazi.Platform.Auth;
using YeaKazi.Platform.Provisioning;
using YeaKazi.Platform.Users;

namespace YeaKazi.Platform.Endpoints;

public static class PlatformEndpoints
{
    public const string TenantPolicy = "tenant";
    public const string PlatformPolicy = "platform";
    public const string AuthRateLimit = "auth";

    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/v1/auth").WithTags("Auth").AllowAnonymous().RequireRateLimiting(AuthRateLimit);
        auth.MapPost("/login", async Task<Ok<LoginResponse>> (LoginRequest body, TenantAuthService svc, CancellationToken ct)
            => TypedResults.Ok(await svc.LoginAsync(body, ct))).WithName("Login");
        auth.MapPost("/mfa/enroll", async Task<Ok<MfaEnrollResponse>> (MfaEnrollRequest body, TenantAuthService svc, CancellationToken ct)
            => TypedResults.Ok(await svc.BeginEnrollmentAsync(body, ct))).WithName("BeginMfaEnrollment");
        auth.MapPost("/mfa/verify", async Task<Ok<LoginResponse>> (MfaVerifyRequest body, TenantAuthService svc, CancellationToken ct)
            => TypedResults.Ok(await svc.VerifyMfaAsync(body, ct))).WithName("VerifyMfa");
        auth.MapPost("/mfa/recover", async Task<Ok<LoginResponse>> (MfaRecoverRequest body, TenantAuthService svc, CancellationToken ct)
            => TypedResults.Ok(await svc.RecoverWithCodeAsync(body, ct))).WithName("RecoverMfa");
        auth.MapPost("/activate", async Task<NoContent> (ActivateRequest body, TenantAuthService svc, CancellationToken ct) =>
        {
            await svc.ActivateAsync(body, ct);
            return TypedResults.NoContent();
        }).WithName("ActivateAccount");

        var tenant = app.MapGroup("/api/v1").RequireAuthorization(TenantPolicy);
        tenant.MapGet("/me", async Task<Ok<MeDto>> (UserAdminService svc, CancellationToken ct)
            => TypedResults.Ok(await svc.MeAsync(ct))).WithTags("Me").WithName("GetMe");
        tenant.MapPost("/me/mfa/recovery-codes", async Task<Ok<RecoveryCodesResponse>> (RegenerateRecoveryCodesRequest body, TenantAuthService svc, CancellationToken ct)
            => TypedResults.Ok(await svc.RegenerateRecoveryCodesAsync(body, ct))).WithTags("Me").WithName("RegenerateRecoveryCodes")
            .RequireRateLimiting(AuthRateLimit);

        var users = tenant.MapGroup("/users").WithTags("Users");
        users.MapGet("/", async Task<Ok<PagedResult<UserDto>>> (string? search, int? page, int? pageSize, UserAdminService svc, CancellationToken ct)
            => TypedResults.Ok(await svc.ListAsync(search, page, pageSize, ct))).WithName("ListUsers");
        users.MapGet("/{id:guid}", async Task<Ok<UserDto>> (Guid id, UserAdminService svc, CancellationToken ct)
            => TypedResults.Ok(await svc.GetAsync(id, ct))).WithName("GetUser");
        users.MapPost("/", async Task<Created<UserDto>> (InviteUserRequest body, UserAdminService svc, CancellationToken ct) =>
        {
            var user = await svc.InviteAsync(body, ct);
            return TypedResults.Created($"/api/v1/users/{user.Id}", user);
        }).WithName("InviteUser");
        users.MapPost("/{id:guid}/roles", async Task<Ok<UserDto>> (Guid id, RoleGrant body, UserAdminService svc, CancellationToken ct)
            => TypedResults.Ok(await svc.AssignRoleAsync(id, body, ct))).WithName("AssignUserRole");
        users.MapPost("/{id:guid}/roles/{assignmentId:guid}/revoke", async Task<Ok<UserDto>> (Guid id, Guid assignmentId, RevokeRoleRequest body, UserAdminService svc, CancellationToken ct)
            => TypedResults.Ok(await svc.RevokeRoleAsync(id, assignmentId, body, ct))).WithName("RevokeUserRole");
        users.MapPost("/{id:guid}/mfa/reset", async Task<Ok<UserDto>> (Guid id, ResetMfaRequest body, UserAdminService svc, CancellationToken ct)
            => TypedResults.Ok(await svc.ResetMfaAsync(id, body, ct))).WithName("ResetUserMfa");
        users.MapPost("/{id:guid}/disable", async Task<Ok<UserDto>> (Guid id, DisableUserRequest body, UserAdminService svc, CancellationToken ct)
            => TypedResults.Ok(await svc.DisableAsync(id, body, ct))).WithName("DisableUser");

        var roles = tenant.MapGroup("/roles").WithTags("Roles");
        roles.MapGet("/", async Task<Ok<IReadOnlyList<RoleDto>>> (RoleService svc, CancellationToken ct)
            => TypedResults.Ok(await svc.ListAsync(ct))).WithName("ListRoles");
        roles.MapGet("/permissions", Ok<IReadOnlyList<PermissionDto>> () => TypedResults.Ok(RoleService.Catalogue())).WithName("ListPermissions");
        roles.MapPost("/", async Task<Created<RoleDto>> (CreateRoleRequest body, RoleService svc, CancellationToken ct) =>
        {
            var role = await svc.CreateAsync(body, ct);
            return TypedResults.Created($"/api/v1/roles/{role.Id}", role);
        }).WithName("CreateRole");
        roles.MapPut("/{id:guid}", async Task<Ok<RoleDto>> (Guid id, UpdateRoleRequest body, RoleService svc, CancellationToken ct)
            => TypedResults.Ok(await svc.UpdateAsync(id, body, ct))).WithName("UpdateRole");

        var audit = tenant.MapGroup("/audit-events").WithTags("Audit");
        audit.MapGet("/", async Task<Ok<PagedResult<AuditEventDto>>> ([AsParameters] AuditSearch search, AuditQueryService svc, CancellationToken ct)
            => TypedResults.Ok(await svc.SearchAsync(search, ct))).WithName("SearchAuditEvents");
        audit.MapPost("/verify-chain", async Task<Ok<AuditChainStatus>> (AuditQueryService svc, CancellationToken ct)
            => TypedResults.Ok(await svc.VerifyChainAsync(ct))).WithName("VerifyAuditChain");

        var platformAuth = app.MapGroup("/api/v1/platform/auth").WithTags("Platform auth").AllowAnonymous().RequireRateLimiting(AuthRateLimit);
        platformAuth.MapPost("/login", async Task<Ok<LoginResponse>> (PlatformLoginRequest body, PlatformAuthService svc, CancellationToken ct)
            => TypedResults.Ok(await svc.LoginAsync(body, ct))).WithName("PlatformLogin");
        platformAuth.MapPost("/mfa/enroll", async Task<Ok<MfaEnrollResponse>> (MfaEnrollRequest body, PlatformAuthService svc, CancellationToken ct)
            => TypedResults.Ok(await svc.BeginEnrollmentAsync(body, ct))).WithName("PlatformBeginMfaEnrollment");
        platformAuth.MapPost("/mfa/verify", async Task<Ok<LoginResponse>> (MfaVerifyRequest body, PlatformAuthService svc, CancellationToken ct)
            => TypedResults.Ok(await svc.VerifyMfaAsync(body, ct))).WithName("PlatformVerifyMfa");

        var platform = app.MapGroup("/api/v1/platform").WithTags("Platform console").RequireAuthorization(PlatformPolicy);
        platform.MapGet("/tenants", async Task<Ok<PagedResult<TenantSummaryDto>>> (string? search, int? page, int? pageSize, ClaimsPrincipal user, TenantProvisioningService svc, CancellationToken ct) =>
        {
            svc.PlatformRole = user.FindFirstValue(AuthClaims.PlatformRole);
            return TypedResults.Ok(await svc.ListAsync(search, page, pageSize, ct));
        }).WithName("ListTenants");
        platform.MapGet("/hosting-regions", Ok<IReadOnlyList<string>> (ClaimsPrincipal user, TenantProvisioningService svc) =>
        {
            svc.PlatformRole = user.FindFirstValue(AuthClaims.PlatformRole);
            return TypedResults.Ok(svc.HostingRegions());
        }).WithName("ListHostingRegions");
        platform.MapPost("/tenants", async Task<Results<Created<ProvisionTenantResponse>, Ok<ProvisionTenantResponse>>> (
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            ProvisionTenantRequest body,
            ClaimsPrincipal user,
            HttpResponse response,
            TenantProvisioningService svc,
            CancellationToken ct) =>
        {
            svc.PlatformRole = user.FindFirstValue(AuthClaims.PlatformRole);
            var outcome = await svc.ProvisionAsync(idempotencyKey, body, ct);
            if (outcome.Replayed)
            {
                response.Headers["Idempotent-Replayed"] = "true";
                return TypedResults.Ok(outcome.Response);
            }

            return TypedResults.Created($"/api/v1/platform/tenants/{outcome.Response.TenantId}", outcome.Response);
        }).WithName("ProvisionTenant");

        return app;
    }
}
