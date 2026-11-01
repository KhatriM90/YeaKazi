using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using YeaKazi.BuildingBlocks.Tenancy;

namespace YeaKazi.Platform.Persistence;

/// <summary>Used only by "dotnet ef" to create migrations.</summary>
public sealed class PlatformDesignTimeFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("YEAKAZI_MIGRATIONS_CONNECTION")
                 ?? "Host=localhost;Database=yeakazi_design;Username=yeakazi_owner;Password=design";
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(cs, o => o.MigrationsHistoryTable("__ef_migrations_history", PlatformDbContext.PlatformSchema))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new PlatformDbContext(options, new RequestContext());
    }
}
