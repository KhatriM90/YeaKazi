using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using YeaKazi.BuildingBlocks.Tenancy;

namespace YeaKazi.People.Persistence;

/// <summary>Used only by "dotnet ef" to create migrations.</summary>
public sealed class PeopleDesignTimeFactory : IDesignTimeDbContextFactory<PeopleDbContext>
{
    public PeopleDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("YEAKAZI_MIGRATIONS_CONNECTION")
                 ?? "Host=localhost;Database=yeakazi_design;Username=yeakazi_owner;Password=design";
        var options = new DbContextOptionsBuilder<PeopleDbContext>()
            .UseNpgsql(cs, o => o.MigrationsHistoryTable("__ef_migrations_history", PeopleDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new PeopleDbContext(options, new RequestContext());
    }
}
