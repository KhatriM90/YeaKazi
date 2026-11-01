using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YeaKazi.BuildingBlocks.Audit;
using YeaKazi.BuildingBlocks.Tenancy;

namespace YeaKazi.BuildingBlocks.Persistence;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the data source, request context and shared per-scope connection.</summary>
    public static IServiceCollection AddYeaKaziPersistence(this IServiceCollection services, string connectionString)
    {
        // Built lazily so tooling (OpenAPI generation, design-time) can start without a database.
        services.AddSingleton(_ =>
        {
            var builder = new NpgsqlDataSourceBuilder(connectionString);
            builder.EnableDynamicJson();
            return builder.Build();
        });
        services.AddScoped<RequestContext>();
        services.AddScoped<SharedDbConnection>();
        services.AddScoped<TenantSessionInterceptor>();
        services.AddScoped<AuditTrail>();
        return services;
    }

    /// <summary>Registers a module DbContext on the shared connection with the tenant session interceptor.</summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, string migrationsHistorySchema)
        where TContext : ModuleDbContext
    {
        services.AddDbContext<TContext>((sp, options) =>
        {
            var shared = sp.GetRequiredService<SharedDbConnection>();
            options.UseNpgsql(shared.Connection, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", migrationsHistorySchema))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>());
        });
        return services;
    }
}
