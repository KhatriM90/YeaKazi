using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using YeaKazi.BuildingBlocks.Persistence;
using YeaKazi.BuildingBlocks.Security;
using YeaKazi.Platform.Access;
using YeaKazi.Platform.AuditLog;
using YeaKazi.Platform.Auth;
using YeaKazi.Platform.Persistence;
using YeaKazi.Platform.Provisioning;
using YeaKazi.Platform.Rules;
using YeaKazi.BuildingBlocks.Contracts;
using YeaKazi.Platform.Users;

namespace YeaKazi.Platform;

public static class PlatformModule
{
    public static IServiceCollection AddPlatformModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<PlatformDbContext>(PlatformDbContext.PlatformSchema);
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.Section));
        services.Configure<PlatformOptions>(configuration.GetSection(PlatformOptions.Section));
        services.AddSingleton<TokenService>();
        services.AddFieldEncryption(configuration);
        services.TryAddTimeProvider();
        services.AddScoped<AccessControl>();
        services.AddScoped<IAccessControl>(sp => sp.GetRequiredService<AccessControl>());
        services.AddScoped<TenantAuthService>();
        services.AddScoped<PlatformAuthService>();
        services.AddScoped<UserAdminService>();
        services.AddScoped<RoleService>();
        services.AddScoped<AuditQueryService>();
        services.AddScoped<TenantProvisioningService>();
        services.AddScoped<IRuleCatalog, RuleCatalog>();
        services.AddScoped<MfaRecoveryService>();
        return services;
    }

    public static IServiceCollection AddFieldEncryption(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("Security:FieldEncryption");
        var active = section["ActiveKeyId"] ?? string.Empty;
        var keys = section.GetSection("Keys").GetChildren().ToDictionary(c => c.Key, c => c.Value ?? string.Empty);
        services.AddSingleton<IFieldEncryptor>(_ => new AesGcmFieldEncryptor(active, keys));
        return services;
    }

    private static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (services.All(d => d.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}
