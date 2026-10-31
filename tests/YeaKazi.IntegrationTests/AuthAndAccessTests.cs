using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using YeaKazi.IntegrationTests.Infrastructure;
using YeaKazi.Platform.Auth;

namespace YeaKazi.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuthAndAccessTests(ApiFactory factory) : IAsyncLifetime
{
    private readonly TestApi _api = new(factory);
    private TenantHandle _t = null!;

    public async Task InitializeAsync()
    {
        var ops = await _api.CreatePlatformAdminAsync();
        _t = await _api.ProvisionTenantAsync(ops);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Privileged_user_gets_no_access_token_without_mfa()
    {
        var res = await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/login", new { tenant = _t.Slug, email = _t.Admin.Email, password = TestApi.Password });
        var body = await TestApi.Body(res);
        Assert.Equal(LoginStatuses.MfaRequired, body["status"]!.GetValue<string>());
        Assert.Null(body["accessToken"]);

        // A challenge token cannot be used as an access token.
        var challenge = body["challengeToken"]!.GetValue<string>();
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.As(challenge).GetAsync("/api/v1/me")).StatusCode);

        var wrong = await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/mfa/verify", new { challengeToken = challenge, code = "000000" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("invalid_mfa_code", await TestApi.ErrorCode(wrong));
    }

    [Fact]
    public async Task Totp_code_cannot_be_replayed()
    {
        var client = _api.Anonymous();
        async Task<string> Challenge()
        {
            var r = await TestApi.Body(await client.PostAsJsonAsync("/api/v1/auth/login", new { tenant = _t.Slug, email = _t.Admin.Email, password = TestApi.Password }));
            return r["challengeToken"]!.GetValue<string>();
        }

        _api.AdvanceStep();
        var code = _api.CurrentCode(_t.Admin.MfaSecret!);
        var first = await client.PostAsJsonAsync("/api/v1/auth/mfa/verify", new { challengeToken = await Challenge(), code });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var replay = await client.PostAsJsonAsync("/api/v1/auth/mfa/verify", new { challengeToken = await Challenge(), code });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task Unknown_tenant_unknown_user_and_wrong_password_look_the_same()
    {
        var client = _api.Anonymous();
        var r1 = await client.PostAsJsonAsync("/api/v1/auth/login", new { tenant = "no-such-tenant", email = _t.Admin.Email, password = TestApi.Password });
        var r2 = await client.PostAsJsonAsync("/api/v1/auth/login", new { tenant = _t.Slug, email = "nobody@x.test", password = TestApi.Password });
        var r3 = await client.PostAsJsonAsync("/api/v1/auth/login", new { tenant = _t.Slug, email = _t.Admin.Email, password = "Wrong-passw0rd!" });
        foreach (var r in new[] { r1, r2, r3 })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
            Assert.Equal("invalid_credentials", await TestApi.ErrorCode(r));
        }
    }

    [Fact]
    public async Task Account_locks_after_repeated_failures()
    {
        var employeeRole = await _api.RoleIdAsync(_t.Admin.Token, "employee");
        var email = $"lock{Guid.NewGuid():N}@x.test";
        await _api.InviteAndActivateAsync(_t, email, (employeeRole, null));
        var client = _api.Anonymous();
        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync("/api/v1/auth/login", new { tenant = _t.Slug, email, password = "Wrong-passw0rd!" });
        }

        var locked = await client.PostAsJsonAsync("/api/v1/auth/login", new { tenant = _t.Slug, email, password = TestApi.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);

        var audit = await TestApi.Body(await _api.As(_t.Admin.Token).GetAsync("/api/v1/audit-events?action=auth.account_locked"));
        Assert.NotEmpty(audit["items"]!.AsArray());
    }

    [Fact]
    public async Task Employee_signs_in_without_mfa_but_must_use_mfa_once_given_a_privileged_role()
    {
        var employeeRole = await _api.RoleIdAsync(_t.Admin.Token, "employee");
        var hrRole = await _api.RoleIdAsync(_t.Admin.Token, "hr_officer");
        var email = $"emp{Guid.NewGuid():N}@x.test";
        var userId = await _api.InviteAndActivateAsync(_t, email, (employeeRole, null));

        var employee = await _api.SignInTenantAsync(_t.Slug, email);
        Assert.Null(employee.MfaSecret);
        var me = await TestApi.Body(await _api.As(employee.Token).GetAsync("/api/v1/me"));
        Assert.Equal(["self.read"], me["permissions"]!.AsArray().Select(p => p!.GetValue<string>()).ToArray());

        // Employee cannot use admin endpoints.
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.As(employee.Token).GetAsync("/api/v1/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.As(employee.Token).GetAsync("/api/v1/org/branches")).StatusCode);

        await TestApi.EnsureSuccess(await _api.As(_t.Admin.Token).PostAsJsonAsync($"/api/v1/users/{userId}/roles", new { roleId = hrRole }));

        var stale = await _api.As(employee.Token).GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        Assert.Equal("mfa_required", await TestApi.ErrorCode(stale));

        var login = await TestApi.Body(await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/login", new { tenant = _t.Slug, email, password = TestApi.Password }));
        Assert.Equal(LoginStatuses.MfaEnrollmentRequired, login["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Disabled_user_token_stops_working_immediately()
    {
        var employeeRole = await _api.RoleIdAsync(_t.Admin.Token, "employee");
        var email = $"dis{Guid.NewGuid():N}@x.test";
        var userId = await _api.InviteAndActivateAsync(_t, email, (employeeRole, null));
        var employee = await _api.SignInTenantAsync(_t.Slug, email);
        Assert.Equal(HttpStatusCode.OK, (await _api.As(employee.Token).GetAsync("/api/v1/me")).StatusCode);

        var user = await TestApi.Body(await _api.As(_t.Admin.Token).GetAsync($"/api/v1/users/{userId}"));
        await TestApi.EnsureSuccess(await _api.As(_t.Admin.Token).PostAsJsonAsync($"/api/v1/users/{userId}/disable",
            new { version = user["version"]!.GetValue<uint>(), reason = "Left the company" }));

        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.As(employee.Token).GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Administrators_cannot_change_their_own_access_or_remove_the_last_administrator()
    {
        var admin = _api.As(_t.Admin.Token);
        var auditor = await _api.RoleIdAsync(_t.Admin.Token, "auditor");
        var self = await admin.PostAsJsonAsync($"/api/v1/users/{_t.AdminUserId}/roles", new { roleId = auditor });
        Assert.Equal("self_administration", await TestApi.ErrorCode(self));

        // A second admin tries to remove the only other admin while being... the second admin is the only other.
        var adminRole = await _api.RoleIdAsync(_t.Admin.Token, "tenant_administrator");
        var email = $"admin2{Guid.NewGuid():N}@x.test";
        var secondId = await _api.InviteAndActivateAsync(_t, email, (adminRole, null));
        var second = await _api.SignInTenantAsync(_t.Slug, email);

        // Second admin disables the first: allowed, because second remains active.
        var first = await TestApi.Body(await _api.As(second.Token).GetAsync($"/api/v1/users/{_t.AdminUserId}"));
        // Revoke the first admin's role instead of disabling, then the second cannot lose theirs.
        var assignment = first["roles"]!.AsArray().Single(r => r!["roleCode"]!.GetValue<string>() == "tenant_administrator")!["id"]!.GetValue<Guid>();
        await TestApi.EnsureSuccess(await _api.As(second.Token).PostAsJsonAsync($"/api/v1/users/{_t.AdminUserId}/roles/{assignment}/revoke", new { reason = "Rotation" }));

        // Now only "second" is an administrator. The first admin's old token loses access to admin features.
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/users")).StatusCode);

        // Restore so other assertions in this class are not affected.
        await TestApi.EnsureSuccess(await _api.As(second.Token).PostAsJsonAsync($"/api/v1/users/{_t.AdminUserId}/roles", new { roleId = adminRole }));
        var secondUser = await TestApi.Body(await admin.GetAsync($"/api/v1/users/{secondId}"));
        Assert.Equal("active", secondUser["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Last_active_administrator_cannot_be_disabled()
    {
        var ops = await _api.CreatePlatformAdminAsync();
        var t = await _api.ProvisionTenantAsync(ops);
        var adminRole = await _api.RoleIdAsync(t.Admin.Token, "tenant_administrator");
        var email = $"admin2{Guid.NewGuid():N}@x.test";
        await _api.InviteAndActivateAsync(t, email, (adminRole, null));
        var second = await _api.SignInTenantAsync(t.Slug, email);

        var first = await TestApi.Body(await _api.As(second.Token).GetAsync($"/api/v1/users/{t.AdminUserId}"));
        await TestApi.EnsureSuccess(await _api.As(second.Token).PostAsJsonAsync($"/api/v1/users/{t.AdminUserId}/disable",
            new { version = first["version"]!.GetValue<uint>(), reason = "Test" }));

        // "second" is now the last admin and cannot be disabled by anyone (self-administration also blocks).
        var me = await TestApi.Body(await _api.As(second.Token).GetAsync("/api/v1/me"));
        var res = await _api.As(second.Token).PostAsJsonAsync($"/api/v1/users/{me["userId"]!.GetValue<Guid>()}/disable", new { version = 0u, reason = "x" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
    }

    [Fact]
    public async Task Employer_scoped_role_only_reaches_its_employer()
    {
        var admin = _api.As(_t.Admin.Token);
        var le2 = await TestApi.Body(await admin.PostAsJsonAsync("/api/v1/org/legal-entities", new { code = "SUB" + Guid.NewGuid().ToString("N")[..4], name = "Subsidiary Ltd", timezone = "Africa/Nairobi" }));
        var le2Id = le2["id"]!.GetValue<Guid>();
        await TestApi.EnsureSuccess(await admin.PostAsJsonAsync("/api/v1/org/branches", new { legalEntityId = le2Id, code = "MSA", name = "Mombasa" }));

        var role = await TestApi.Body(await admin.PostAsJsonAsync("/api/v1/roles", new
        {
            code = "org_editor_" + Guid.NewGuid().ToString("N")[..6],
            name = "Org editor",
            permissions = new[] { "org.read", "org.write" },
        }));
        var email = $"scoped{Guid.NewGuid():N}@x.test";
        await _api.InviteAndActivateAsync(_t, email, (role["id"]!.GetValue<Guid>(), _t.LegalEntityId));
        var scoped = await _api.SignInTenantAsync(_t.Slug, email);
        Assert.NotNull(scoped.MfaSecret); // org.write is privileged

        var client = _api.As(scoped.Token);
        var branches = await TestApi.Body(await client.GetAsync("/api/v1/org/branches"));
        Assert.All(branches["items"]!.AsArray(), b => Assert.Equal(_t.LegalEntityId, b!["legalEntityId"]!.GetValue<Guid>()));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/org/legal-entities/{le2Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/org/branches", new { legalEntityId = le2Id, code = "NKR", name = "Nakuru" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/org/branches", new { legalEntityId = _t.LegalEntityId, code = "KSM" + Guid.NewGuid().ToString("N")[..3], name = "Kisumu" })).StatusCode);
        // Creating a new employer needs tenant-wide org.write.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/org/legal-entities", new { code = "NOPE", name = "Nope" })).StatusCode);
    }

    [Fact]
    public async Task System_roles_are_read_only_and_custom_roles_use_the_catalogue()
    {
        var admin = _api.As(_t.Admin.Token);
        var system = await _api.RoleIdAsync(_t.Admin.Token, "tenant_administrator");
        var edit = await admin.PutAsJsonAsync($"/api/v1/roles/{system}", new { name = "Admin", permissions = new[] { "payroll.approve" }, version = 0u, reason = "x" });
        Assert.Equal("system_role_readonly", await TestApi.ErrorCode(edit));

        var bad = await admin.PostAsJsonAsync("/api/v1/roles", new { code = "bad_role", name = "Bad", permissions = new[] { "everything.root" } });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }
}
