using YeaKazi.Platform.Auth;
using YeaKazi.Platform.Domain;

namespace YeaKazi.Api;

/// <summary>
/// Operator commands:
///   migrate                                   apply all migrations (ConnectionStrings:Migrations)
///   create-platform-admin --email E --name N  create a platform administrator; password from YEAKAZI_BOOTSTRAP_PASSWORD
/// </summary>
public static class CommandLine
{
    public static async Task<bool> TryRunAsync(WebApplication app, string[] args)
    {
        switch (args[0])
        {
            case "migrate":
                var owner = app.Configuration.GetConnectionString("Migrations")
                            ?? throw new InvalidOperationException("ConnectionStrings:Migrations is not configured.");
                await DatabaseMigrator.MigrateAsync(owner);
                Console.WriteLine("Migrations applied.");
                return true;
            case "create-platform-admin":
                var email = Arg(args, "--email");
                var name = Arg(args, "--name");
                var password = Environment.GetEnvironmentVariable("YEAKAZI_BOOTSTRAP_PASSWORD")
                               ?? throw new InvalidOperationException("Set YEAKAZI_BOOTSTRAP_PASSWORD.");
                using (var scope = app.Services.CreateScope())
                {
                    var svc = scope.ServiceProvider.GetRequiredService<PlatformAuthService>();
                    var id = await svc.CreatePlatformUserAsync(email, name, PlatformRoles.Admin, password, CancellationToken.None);
                    Console.WriteLine($"Platform administrator {id} created. Sign in to enrol MFA.");
                }

                return true;
            default:
                return false;
        }
    }

    private static string Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : throw new ArgumentException($"Missing {name}.");
    }
}
