using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using YeaKazi.IntegrationTests.Infrastructure;
using YeaKazi.Platform.Auth;

namespace YeaKazi.IntegrationTests;

/// <summary>S2 additions: rule-driven Kenya holidays, ad-hoc government announcements, MFA recovery codes and admin MFA reset.</summary>
[Collection(ApiCollection.Name)]
public class HolidayAndMfaRecoveryTests(ApiFactory factory) : IAsyncLifetime
{
    private readonly TestApi _api = new(factory);
    private TenantHandle _t = null!;

    public async Task InitializeAsync()
    {
        var ops = await _api.CreatePlatformAdminAsync();
        _t = await _api.ProvisionTenantAsync(ops);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Admin => _api.As(_t.Admin.Token);

    private async Task<JsonObject> KePublicCalendar()
    {
        var cals = await TestApi.Body(await Admin.GetAsync("/api/v1/org/holiday-calendars"));
        return cals["items"]!.AsArray().Single(c => c!["code"]!.GetValue<string>() == "KE-PUBLIC")!.AsObject();
    }

    private async Task<List<JsonNode>> Holidays(Guid calendarId, int year)
        => (await Admin.GetFromJsonAsync<JsonArray>($"/api/v1/org/holiday-calendars/{calendarId}/holidays?year={year}", TestApi.Json))!.Select(x => x!).ToList();

    [Fact]
    public async Task Provisioning_seeds_kenya_holidays_from_the_rule_version_and_applies_the_sunday_rule()
    {
        var cal = await KePublicCalendar();
        var id = cal["id"]!.GetValue<Guid>();
        var thisYear = DateTime.UtcNow.Year;
        var seeded = await Holidays(id, thisYear);
        Assert.Equal(8, seeded.Count);
        Assert.All(seeded, h => Assert.Equal("KE calendar.public_holidays 2026.1", h["ruleVersion"]!.GetValue<string>()));
        Assert.All(seeded, h => Assert.Contains("Public Holidays Act", h["sourceReference"]!.GetValue<string>()));

        // 2027: Mazingira Day (10 Oct), Jamhuri Day (12 Dec) and Boxing Day (26 Dec) fall on Sundays (s.4 Cap 110).
        var load = await TestApi.Body(await Admin.PostAsJsonAsync($"/api/v1/org/holiday-calendars/{id}/load-statutory", new { year = 2027 }));
        Assert.Contains("Good Friday", load["notGenerated"]!.AsArray().Select(x => x!.GetValue<string>()));
        var dates = (await Holidays(id, 2027)).Select(h => h["date"]!.GetValue<string>()).ToHashSet();
        Assert.DoesNotContain("2027-10-10", dates);
        Assert.Contains("2027-10-11", dates);
        Assert.Contains("2027-12-13", dates);
        Assert.Contains("2027-12-27", dates);
        Assert.Equal(8, dates.Count);

        // Loading again adds nothing.
        var again = await TestApi.Body(await Admin.PostAsJsonAsync($"/api/v1/org/holiday-calendars/{id}/load-statutory", new { year = 2027 }));
        Assert.Empty(again["added"]!.AsArray());

        // A year before the first rule version is refused rather than guessed.
        var early = await Admin.PostAsJsonAsync($"/api/v1/org/holiday-calendars/{id}/load-statutory", new { year = 2020 });
        Assert.True(TestApi.IsDenied(early.StatusCode));
    }

    [Fact]
    public async Task Government_can_announce_an_ad_hoc_holiday_with_a_gazette_reference()
    {
        var cal = await KePublicCalendar();
        var noSource = await Admin.PostAsJsonAsync("/api/v1/org/holidays/announce", new { date = "2026-11-20", name = "National day of prayer", kind = "short_notice", sourceReference = "" });
        Assert.Equal(HttpStatusCode.BadRequest, noSource.StatusCode);

        var res = await TestApi.Body(await Admin.PostAsJsonAsync("/api/v1/org/holidays/announce",
            new { date = "2026-11-20", name = "National day of prayer", kind = "short_notice", sourceReference = "Gazette Notice No. 9999 of 2026" }));
        Assert.Single(res["added"]!.AsArray());
        var h = (await Holidays(cal["id"]!.GetValue<Guid>(), 2026)).Single(x => x["date"]!.GetValue<string>() == "2026-11-20");
        Assert.Equal("Gazette Notice No. 9999 of 2026", h["sourceReference"]!.GetValue<string>());

        // Announcing the same day again skips calendars that already have it.
        var dup = await TestApi.Body(await Admin.PostAsJsonAsync("/api/v1/org/holidays/announce",
            new { date = "2026-11-20", name = "National day of prayer", kind = "short_notice", sourceReference = "Gazette Notice No. 9999 of 2026" }));
        Assert.Empty(dup["added"]!.AsArray());

        var audit = await TestApi.Body(await Admin.GetAsync("/api/v1/audit-events?action=org.holiday.created&pageSize=50"));
        Assert.Contains(audit["items"]!.AsArray(), e => e!["after"]?["sourceReference"]?.GetValue<string>() == "Gazette Notice No. 9999 of 2026");
    }

    [Fact]
    public async Task Recovery_codes_sign_in_once_regenerate_and_admin_reset()
    {
        var hrRole = await _api.RoleIdAsync(_t.Admin.Token, "hr_officer");
        var email = $"hr@{_t.Slug}.test";
        var userId = await _api.InviteAndActivateAsync(_t, email, (hrRole, null));
        var first = await _api.SignInTenantAsync(_t.Slug, email);
        Assert.NotNull(first.RecoveryCodes);
        Assert.Equal(10, first.RecoveryCodes!.Count);
        var code = first.RecoveryCodes[0];

        async Task<LoginResponse> Login()
        {
            var r = await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/login", new { tenant = _t.Slug, email, password = TestApi.Password });
            return (await TestApi.Body(r)).Deserialize<LoginResponse>(TestApi.Json)!;
        }

        var challenge = await Login();
        Assert.Equal(LoginStatuses.MfaRequired, challenge.Status);
        var recovered = await TestApi.Body(await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/mfa/recover",
            new { challengeToken = challenge.ChallengeToken, recoveryCode = code.ToLowerInvariant().Replace("-", " ") }));
        Assert.Equal(LoginStatuses.Authenticated, recovered["status"]!.GetValue<string>());
        var token = recovered["accessToken"]!.GetValue<string>();
        var me = await TestApi.Body(await _api.As(token).GetAsync("/api/v1/me"));
        Assert.Equal(9, me["recoveryCodesRemaining"]!.GetValue<int>());

        // Single use.
        var reuse = await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/mfa/recover", new { challengeToken = (await Login()).ChallengeToken, recoveryCode = code });
        Assert.False(reuse.IsSuccessStatusCode);

        // The user is told by email that a recovery code was used.
        await factory.DispatchOutboxAsync();
        lock (factory.Email.Sent)
        {
            Assert.Contains(factory.Email.Sent, m => m.To == email && m.TextBody.Contains("recovery code", StringComparison.OrdinalIgnoreCase));
        }

        // Regenerating needs a current authenticator code and invalidates the old set.
        _api.AdvanceStep();
        var fresh = await TestApi.Body(await _api.As(token).PostAsJsonAsync("/api/v1/me/mfa/recovery-codes", new { code = _api.CurrentCode(first.MfaSecret!) }));
        Assert.Equal(10, fresh["recoveryCodes"]!.AsArray().Count);
        var old = await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/mfa/recover", new { challengeToken = (await Login()).ChallengeToken, recoveryCode = first.RecoveryCodes[1] });
        Assert.False(old.IsSuccessStatusCode);

        // An administrator resets MFA (lost phone and codes): reason required, user must enrol again.
        var user = await TestApi.Body(await Admin.GetAsync($"/api/v1/users/{userId}"));
        var noReason = await Admin.PostAsJsonAsync($"/api/v1/users/{userId}/mfa/reset", new { version = user["version"]!.GetValue<uint>(), reason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        await TestApi.EnsureSuccess(await Admin.PostAsJsonAsync($"/api/v1/users/{userId}/mfa/reset",
            new { version = user["version"]!.GetValue<uint>(), reason = "Lost phone, identity checked in person" }));
        Assert.Equal(LoginStatuses.MfaEnrollmentRequired, (await Login()).Status);
        // The old session was revoked with the security stamp.
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.As(token).GetAsync("/api/v1/me")).StatusCode);
        // Admins cannot reset their own MFA.
        var adminUser = await TestApi.Body(await Admin.GetAsync($"/api/v1/users/{_t.AdminUserId}"));
        var selfReset = await Admin.PostAsJsonAsync($"/api/v1/users/{_t.AdminUserId}/mfa/reset", new { version = adminUser["version"]!.GetValue<uint>(), reason = "x" });
        Assert.False(selfReset.IsSuccessStatusCode);
    }
}
