using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using YeaKazi.BuildingBlocks.Tenancy;

namespace YeaKazi.Organization.Persistence;

/// <summary>Used only by "dotnet ef" to create migrations.</summary>
public sealed class OrganizationDesignTimeFactory : IDesignTimeDbContextFactory<OrganizationDbContext>
{
    public OrganizationDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("YEAKAZI_MIGRATIONS_CONNECTION")
                 ?? "Host=localhost;Database=yeakazi_design;Username=yeakazi_owner;Password=design";
        var options = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseNpgsql(cs, o => o.MigrationsHistoryTable("__ef_migrations_history", OrganizationDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new OrganizationDbContext(options, new RequestContext());
    }
}
