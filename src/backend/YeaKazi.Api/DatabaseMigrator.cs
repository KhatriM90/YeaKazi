using Microsoft.EntityFrameworkCore;
using YeaKazi.BuildingBlocks.Tenancy;
using YeaKazi.Organization.Persistence;
using YeaKazi.People.Persistence;
using YeaKazi.Platform.Persistence;

namespace YeaKazi.Api;

/// <summary>
/// Applies every module's migrations in dependency order using the schema-owner
/// connection. The application itself never runs with owner rights.
/// </summary>
public static class DatabaseMigrator
{
    public static async Task MigrateAsync(string ownerConnectionString, CancellationToken ct = default)
    {
        var platform = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(ownerConnectionString, o => o.MigrationsHistoryTable("__ef_migrations_history", PlatformDbContext.PlatformSchema))
            .UseSnakeCaseNamingConvention().Options;
        await using (var db = new PlatformDbContext(platform, new RequestContext()))
        {
            await db.Database.MigrateAsync(ct);
        }

        var org = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseNpgsql(ownerConnectionString, o => o.MigrationsHistoryTable("__ef_migrations_history", OrganizationDbContext.Schema))
            .UseSnakeCaseNamingConvention().Options;
        await using (var db = new OrganizationDbContext(org, new RequestContext()))
        {
            await db.Database.MigrateAsync(ct);
        }

        var people = new DbContextOptionsBuilder<PeopleDbContext>()
            .UseNpgsql(ownerConnectionString, o => o.MigrationsHistoryTable("__ef_migrations_history", PeopleDbContext.Schema))
            .UseSnakeCaseNamingConvention().Options;
        await using (var db = new PeopleDbContext(people, new RequestContext()))
        {
            await db.Database.MigrateAsync(ct);
        }
    }
}
