using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using YeaKazi.BuildingBlocks.Security;
using YeaKazi.Platform.Auth;
using YeaKazi.Workers.Email;
using YeaKazi.Workers.Outbox;

namespace YeaKazi.IntegrationTests.Infrastructure;

/// <summary>Moves forward one TOTP step on demand so several sign-ins of one user in a test do not trip replay protection.</summary>
public sealed class TestClock : TimeProvider
{
    private long _offsetSeconds;

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddSeconds(Interlocked.Read(ref _offsetSeconds));

    public void Advance(TimeSpan by) => Interlocked.Add(ref _offsetSeconds, (long)by.TotalSeconds);
}

public sealed class CapturingEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        lock (Sent)
        {
            Sent.Add(message);
        }

        return Task.CompletedTask;
    }
}

/// <summary>One database and one API host shared by all integration tests.</summary>
public sealed class ApiFactory : WebApplicationFactory<YeaKazi.Api.ApiEntryPoint>, IAsyncLifetime
{
    public const string HostingRegion = "test-region";
    public static readonly string FieldKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static readonly string SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    public TestDatabase Database { get; } = new();
    public TestClock Clock { get; } = new();
    public CapturingEmailSender Email { get; } = new();

    public async Task InitializeAsync() => await Database.CreateAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await Database.DropAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:App", Database.AppConnectionString);
        builder.UseSetting("Auth:SigningKey", SigningKey);
        builder.UseSetting("Security:FieldEncryption:ActiveKeyId", "t1");
        builder.UseSetting("Security:FieldEncryption:Keys:t1", FieldKey);
        builder.UseSetting("Platform:HostingRegions:0", HostingRegion);
        builder.UseSetting("RateLimits:AuthPerMinute", "100000");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    /// <summary>Runs the real outbox dispatcher on the worker role with a capturing email sender.</summary>
    public async Task<int> DispatchOutboxAsync()
    {
        var encryptor = Services.GetRequiredService<IFieldEncryptor>();
        var auth = Options.Create(new AuthOptions { PortalBaseUrl = "https://portal.test" });
        await using var ds = NpgsqlDataSource.Create(Database.WorkerConnectionString);
        var dispatcher = new OutboxDispatcher(
            new WorkerDataSource(ds),
            [new InvitationEmailHandler(Email, encryptor, auth)],
            Options.Create(new OutboxOptions { BatchSize = 100 }),
            NullLogger<OutboxDispatcher>.Instance);
        var total = 0;
        int n;
        while ((n = await dispatcher.DispatchBatchAsync(CancellationToken.None)) > 0)
        {
            total += n;
        }

        return total;
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
