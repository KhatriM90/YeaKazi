using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;
using Npgsql;
using YeaKazi.BuildingBlocks.Http;
using YeaKazi.BuildingBlocks.Persistence;
using YeaKazi.Organization;
using YeaKazi.Organization.Endpoints;
using YeaKazi.Platform;
using YeaKazi.Platform.Auth;
using YeaKazi.Platform.Endpoints;

namespace YeaKazi.Api;

public static class ApiSetup
{
    public static WebApplicationBuilder AddYeaKaziApi(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var config = builder.Configuration;

        var appConnection = config.GetConnectionString("App") ?? string.Empty;
        services.AddYeaKaziPersistence(appConnection);
        services.AddPlatformModule(config);
        services.AddOrganizationModule();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<TokenService>((options, tokens) =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = tokens.AccessTokenValidation();
            });
        services.AddAuthorizationBuilder()
            .AddPolicy(PlatformEndpoints.TenantPolicy, p => p.RequireAuthenticatedUser().RequireClaim(AuthClaims.Scope, AuthClaims.ScopeTenant))
            .AddPolicy(PlatformEndpoints.PlatformPolicy, p => p.RequireAuthenticatedUser().RequireClaim(AuthClaims.Scope, AuthClaims.ScopePlatform));

        var authPermitsPerMinute = config.GetValue("RateLimits:AuthPerMinute", 20);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(PlatformEndpoints.AuthRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = authPermitsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });

        services.AddOpenApi("v1", options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "YeaKazi API",
                    Version = "v1",
                    Description = "YeaKazi HRM and Kenya payroll (by Yeasoft). Errors are RFC 7807 problem details with a stable 'code' and 'correlationId'.",
                };
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                };
                return Task.CompletedTask;
            });
        });

        services.AddHealthChecks();
        return builder;
    }

    public static WebApplication UseYeaKaziApi(this WebApplication app)
    {
        app.Use(async (http, next) =>
        {
            var h = http.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "no-referrer";
            h["Cache-Control"] = "no-store";
            await next();
        });
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<ErrorHandlingMiddleware>();
        app.UseAuthentication();
        app.UseMiddleware<CurrentActorMiddleware>();
        app.UseRateLimiter();
        app.UseAuthorization();

        app.MapOpenApi("/openapi/{documentName}.json");
        app.MapHealthChecks("/health/live").AllowAnonymous();
        app.MapGet("/health/ready", async (NpgsqlDataSource ds, CancellationToken ct) =>
        {
            await using var cmd = ds.CreateCommand("select 1");
            await cmd.ExecuteScalarAsync(ct);
            return Results.Ok(new { status = "ready" });
        }).AllowAnonymous().ExcludeFromDescription();

        app.MapPlatformEndpoints();
        app.MapOrganizationEndpoints(PlatformEndpoints.TenantPolicy);
        return app;
    }
}
