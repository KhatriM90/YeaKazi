using Npgsql;
using YeaKazi.Api;

namespace YeaKazi.IntegrationTests.Infrastructure;

/// <summary>
/// Creates a throw-away PostgreSQL database, applies all migrations as the owner role and
/// exposes connection strings for the app and worker roles. Needs a PostgreSQL superuser
/// connection in YEAKAZI_TEST_PG_ADMIN (default: localhost, postgres/postgres).
/// </summary>
public sealed class TestDatabase
{
    public static readonly string AdminConnectionString =
        Environment.GetEnvironmentVariable("YEAKAZI_TEST_PG_ADMIN")
        ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres";

    private static readonly string OwnerPassword = Environment.GetEnvironmentVariable("YEAKAZI_TEST_OWNER_PASSWORD") ?? "yeakazi_owner_dev";
    private static readonly string AppPassword = Environment.GetEnvironmentVariable("YEAKAZI_TEST_APP_PASSWORD") ?? "yeakazi_app_dev";
    private static readonly string WorkerPassword = Environment.GetEnvironmentVariable("YEAKAZI_TEST_WORKER_PASSWORD") ?? "yeakazi_worker_dev";

    public string Name { get; } = "yeakazi_test_" + Guid.NewGuid().ToString("N")[..12];

    public string OwnerConnectionString => With("yeakazi_owner", OwnerPassword);
    public string AppConnectionString => With("yeakazi_app", AppPassword);
    public string WorkerConnectionString => With("yeakazi_worker", WorkerPassword);
    public string SuperuserConnectionString => new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = Name }.ConnectionString;

    public async Task CreateAsync()
    {
        await using var admin = new NpgsqlConnection(AdminConnectionString);
        await admin.OpenAsync();
        var rolesSql = await File.ReadAllTextAsync(Path.Combine(RepoRoot(), "deploy", "postgres", "00-roles.sql"));
        await using (var cmd = new NpgsqlCommand(rolesSql, admin))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand($"create database {Name} owner yeakazi_owner", admin))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        await DatabaseMigrator.MigrateAsync(OwnerConnectionString);
    }

    public async Task DropAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(AdminConnectionString);
        await admin.OpenAsync();
        await using var cmd = new NpgsqlCommand($"drop database if exists {Name} with (force)", admin);
        await cmd.ExecuteNonQueryAsync();
    }

    private string With(string user, string password)
        => new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = Name, Username = user, Password = password }.ConnectionString;

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "YeaKazi.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
