using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using YeaKazi.BuildingBlocks.Errors;
using YeaKazi.BuildingBlocks.Tenancy;
using YeaKazi.Platform.Access;
using YeaKazi.Platform.Domain;
using YeaKazi.Platform.Persistence;

namespace YeaKazi.Platform.Auth;

/// <summary>
/// Turns a validated JWT into the request context. The tenant comes only from the
/// signed token. Rejects tokens of disabled users, suspended tenants, rotated
/// security stamps, and privileged users who have not completed MFA.
/// </summary>
public sealed class CurrentActorMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, RequestContext context, PlatformDbContext db)
    {
        var user = http.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            var scope = user.FindFirstValue(AuthClaims.Scope);
            var sub = Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
            var stamp = user.FindFirstValue(AuthClaims.SecurityStamp);
            var amr = user.FindFirstValue(AuthClaims.Amr);

            if (scope == AuthClaims.ScopeTenant)
            {
                var tenantId = Guid.Parse(user.FindFirstValue(AuthClaims.TenantId)!);
                context.SetTenant(tenantId);
                var account = await db.Users.AsNoTracking()
                    .Where(u => u.Id == sub)
                    .Select(u => new { u.Status, u.SecurityStamp, u.DisplayName })
                    .SingleOrDefaultAsync(http.RequestAborted);
                var tenantActive = await db.Tenants.AsNoTracking()
                    .AnyAsync(t => t.TenantId == tenantId && t.Status == TenantStatuses.Active, http.RequestAborted);

                if (account is null || account.Status != UserStatuses.Active || account.SecurityStamp != stamp || !tenantActive)
                {
                    throw new UnauthorizedException("session_invalid", "Your session is no longer valid. Sign in again.");
                }

                context.SetTenantUser(tenantId, sub, account.DisplayName);

                if (amr != AuthClaims.AmrMfa)
                {
                    var permissions = await AccessControl.LoadForUserAsync(db, sub, http.RequestAborted);
                    if (AccessControl.RequiresMfa(permissions.Keys))
                    {
                        throw new UnauthorizedException("mfa_required", "Your role requires multi-factor sign-in. Sign in again.");
                    }
                }
            }
            else if (scope == AuthClaims.ScopePlatform)
            {
                context.EnterPlatformScope();
                var account = await db.PlatformUsers.AsNoTracking()
                    .Where(u => u.Id == sub)
                    .Select(u => new { u.IsActive, u.SecurityStamp, u.DisplayName })
                    .SingleOrDefaultAsync(http.RequestAborted);
                if (account is null || !account.IsActive || account.SecurityStamp != stamp || amr != AuthClaims.AmrMfa)
                {
                    throw new UnauthorizedException("session_invalid", "Your session is no longer valid. Sign in again.");
                }

                context.SetPlatformUser(sub, account.DisplayName);
            }
            else
            {
                throw new UnauthorizedException("session_invalid", "Unknown token scope.");
            }
        }

        await next(http);
    }
}
