using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using Microsoft.Extensions.DependencyInjection;
using YeaKazi.Platform.Auth;
using YeaKazi.Platform.Domain;

namespace YeaKazi.IntegrationTests.Infrastructure;

public sealed record SignedIn(string Token, Guid UserId, string Email, string Password, string? MfaSecret);

public sealed record TenantHandle(Guid TenantId, string Slug, Guid LegalEntityId, Guid AdminUserId, SignedIn Admin);

/// <summary>High-level helpers that drive the API exactly as a client would.</summary>
public sealed class TestApi(ApiFactory factory)
{
    public const string Password = "Str0ng-Passw0rd!";
    private static int _platformCounter;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public HttpClient Anonymous() => factory.CreateClient();

    public HttpClient As(string token)
    {
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    public async Task<SignedIn> CreatePlatformAdminAsync()
    {
        var email = $"ops{Interlocked.Increment(ref _platformCounter)}-{Guid.NewGuid():N}@yeasoft.test";
        using var scope = factory.Services.CreateScope();
        var id = await scope.ServiceProvider.GetRequiredService<PlatformAuthService>()
            .CreatePlatformUserAsync(email, "Platform Ops", PlatformRoles.Admin, Password, CancellationToken.None);
        var signedIn = await SignInAsync("/api/v1/platform/auth", new { email, password = Password }, null);
        return signedIn with { UserId = id, Email = email };
    }

    public async Task<TenantHandle> ProvisionTenantAsync(SignedIn platformAdmin, string? slug = null, string? kraPin = null)
    {
        slug ??= "t" + Guid.NewGuid().ToString("N")[..10];
        var adminEmail = $"admin@{slug}.test";
        var response = await As(platformAdmin.Token).SendAsync(ProvisionRequest(slug, adminEmail, Guid.NewGuid().ToString("N"), kraPin));
        await EnsureSuccess(response);
        var body = (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!;
        var tenantId = body["tenantId"]!.GetValue<Guid>();
        var adminId = body["administratorUserId"]!.GetValue<Guid>();
        var leId = body["legalEntityId"]!.GetValue<Guid>();

        await ActivateFromEmailAsync(slug, adminEmail);
        var admin = await SignInTenantAsync(slug, adminEmail);
        return new TenantHandle(tenantId, slug, leId, adminId, admin with { UserId = adminId });
    }

    public static HttpRequestMessage ProvisionRequest(string slug, string adminEmail, string idempotencyKey, string? kraPin = null, string? name = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/platform/tenants")
        {
            Content = JsonContent.Create(new
            {
                name = name ?? $"Tenant {slug}",
                slug,
                hostingRegion = ApiFactory.HostingRegion,
                legalEntity = new { code = "MAIN", name = $"{slug} Ltd", registrationNumber = "CPR/2020/1", kraPin, nssfEmployerNumber = "1234", timezone = "Africa/Nairobi" },
                administrator = new { email = adminEmail, displayName = "First Admin" },
            }),
        };
        req.Headers.Add("Idempotency-Key", idempotencyKey);
        return req;
    }

    /// <summary>Dispatches the outbox, finds the invitation email and sets the password.</summary>
    public async Task ActivateFromEmailAsync(string slug, string email)
    {
        await factory.DispatchOutboxAsync();
        string body;
        lock (factory.Email.Sent)
        {
            body = factory.Email.Sent.Last(m => m.To == email).TextBody;
        }

        var link = body.Split('\n').Select(l => l.Trim()).First(l => l.StartsWith("https://portal.test/activate"));
        var query = HttpUtility.ParseQueryString(new Uri(link).Query);
        var res = await Anonymous().PostAsJsonAsync("/api/v1/auth/activate", new { tenant = slug, token = query["token"], password = Password });
        await EnsureSuccess(res);
    }

    private readonly Dictionary<string, string> _secrets = new();

    public async Task<SignedIn> SignInTenantAsync(string slug, string email)
    {
        _secrets.TryGetValue($"{slug}/{email}", out var secret);
        var s = await SignInAsync("/api/v1/auth", new { tenant = slug, email, password = Password }, secret);
        if (s.MfaSecret is not null)
        {
            _secrets[$"{slug}/{email}"] = s.MfaSecret;
        }

        return s with { Email = email };
    }

    private async Task<SignedIn> SignInAsync(string basePath, object credentials, string? knownSecret)
    {
        var client = Anonymous();
        var login = await client.PostAsJsonAsync($"{basePath}/login", credentials);
        await EnsureSuccess(login);
        var result = (await login.Content.ReadFromJsonAsync<LoginResponse>(Json))!;
        if (result.Status == LoginStatuses.Authenticated)
        {
            return new SignedIn(result.AccessToken!, Guid.Empty, string.Empty, Password, null);
        }

        var secret = knownSecret;
        if (result.Status == LoginStatuses.MfaEnrollmentRequired)
        {
            var enroll = await client.PostAsJsonAsync($"{basePath}/mfa/enroll", new { challengeToken = result.ChallengeToken });
            await EnsureSuccess(enroll);
            secret = (await enroll.Content.ReadFromJsonAsync<MfaEnrollResponse>(Json))!.Secret;
        }

        factory.Clock.Advance(TimeSpan.FromSeconds(Totp.StepSeconds));
        var code = Totp.Compute(Base32.Decode(secret!), Totp.StepAt(factory.Clock.GetUtcNow()));
        var verify = await client.PostAsJsonAsync($"{basePath}/mfa/verify", new { challengeToken = result.ChallengeToken, code });
        await EnsureSuccess(verify);
        var done = (await verify.Content.ReadFromJsonAsync<LoginResponse>(Json))!;
        return new SignedIn(done.AccessToken!, Guid.Empty, string.Empty, Password, secret);
    }

    public string CurrentCode(string secret) => Totp.Compute(Base32.Decode(secret), Totp.StepAt(factory.Clock.GetUtcNow()));

    public void AdvanceStep() => factory.Clock.Advance(TimeSpan.FromSeconds(Totp.StepSeconds));

    public async Task<Guid> RoleIdAsync(string token, string code)
    {
        var roles = await As(token).GetFromJsonAsync<JsonArray>("/api/v1/roles", Json);
        return roles!.Single(r => r!["code"]!.GetValue<string>() == code)!["id"]!.GetValue<Guid>();
    }

    /// <summary>Invites a user with roles, activates them and returns the user id.</summary>
    public async Task<Guid> InviteAndActivateAsync(TenantHandle tenant, string email, params (Guid RoleId, Guid? LegalEntityId)[] roles)
    {
        var res = await As(tenant.Admin.Token).PostAsJsonAsync("/api/v1/users", new
        {
            email,
            displayName = email.Split('@')[0],
            roles = roles.Select(r => new { roleId = r.RoleId, legalEntityId = r.LegalEntityId }).ToArray(),
        });
        await EnsureSuccess(res);
        var id = (await res.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<Guid>();
        await ActivateFromEmailAsync(tenant.Slug, email);
        return id;
    }

    public static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"{(int)response.StatusCode} {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: {body}");
        }
    }

    public static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return JsonNode.Parse(text)?["code"]?.GetValue<string>();
    }

    public static async Task<JsonObject> Body(HttpResponseMessage response)
    {
        await EnsureSuccess(response);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!;
    }

    public static bool IsDenied(HttpStatusCode code) => code is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity;
}
