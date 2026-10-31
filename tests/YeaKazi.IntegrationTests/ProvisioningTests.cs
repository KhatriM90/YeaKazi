using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Npgsql;
using YeaKazi.IntegrationTests.Infrastructure;

namespace YeaKazi.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ProvisioningTests(ApiFactory factory)
{
    private readonly TestApi _api = new(factory);

    [Fact]
    public async Task Provisioning_creates_tenant_employer_roles_and_admin_and_is_audited()
    {
        var ops = await _api.CreatePlatformAdminAsync();
        var tenant = await _api.ProvisionTenantAsync(ops, kraPin: "P051234567X");

        var me = await TestApi.Body(await _api.As(tenant.Admin.Token).GetAsync("/api/v1/me"));
        Assert.Equal(tenant.TenantId, me["tenantId"]!.GetValue<Guid>());
        var permissions = me["permissions"]!.AsArray().Select(p => p!.GetValue<string>()).ToList();
        Assert.Contains("org.write", permissions);
        Assert.DoesNotContain(permissions, p => p.StartsWith("payroll."));

        var roles = await _api.As(tenant.Admin.Token).GetFromJsonAsync<JsonArray>("/api/v1/roles", TestApi.Json);
        Assert.Equal(8, roles!.Count(r => r!["isSystem"]!.GetValue<bool>()));

        var les = await TestApi.Body(await _api.As(tenant.Admin.Token).GetAsync("/api/v1/org/legal-entities"));
        var le = les["items"]!.AsArray().Single()!;
        Assert.Equal("P051234567X", le["kraPin"]!.GetValue<string>());
        Assert.Equal("KES", le["baseCurrency"]!.GetValue<string>());

        var audit = await TestApi.Body(await _api.As(tenant.Admin.Token).GetAsync("/api/v1/audit-events?pageSize=200"));
        var actions = audit["items"]!.AsArray().Select(e => e!["action"]!.GetValue<string>()).ToList();
        Assert.Contains("tenant.provisioned", actions);
        Assert.Contains("role.system_roles_seeded", actions);
        Assert.Contains("org.legal_entity.created", actions);
        Assert.Contains("user.invited", actions);
        Assert.Contains("user.role_assigned", actions);
        Assert.Contains("user.activated", actions);
        Assert.Contains("auth.mfa_enrolled", actions);
    }

    [Fact]
    public async Task Provisioning_is_idempotent_by_key_and_rejects_key_reuse_with_a_different_request()
    {
        var ops = await _api.CreatePlatformAdminAsync();
        var client = _api.As(ops.Token);
        var slug = "idem" + Guid.NewGuid().ToString("N")[..8];
        var key = Guid.NewGuid().ToString("N");

        var first = await client.SendAsync(TestApi.ProvisionRequest(slug, $"a@{slug}.test", key));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstBody = await TestApi.Body(first);

        var replay = await client.SendAsync(TestApi.ProvisionRequest(slug, $"a@{slug}.test", key));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("Idempotent-Replayed").Single());
        var replayBody = await TestApi.Body(replay);
        Assert.Equal(firstBody["tenantId"]!.GetValue<Guid>(), replayBody["tenantId"]!.GetValue<Guid>());

        var conflicting = await client.SendAsync(TestApi.ProvisionRequest(slug, $"a@{slug}.test", key, name: "Different name"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, conflicting.StatusCode);
        Assert.Equal("idempotency_key_reused", await TestApi.ErrorCode(conflicting));

        var duplicateSlug = await client.SendAsync(TestApi.ProvisionRequest(slug, $"b@{slug}.test", Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.Conflict, duplicateSlug.StatusCode);

        await using var db = new NpgsqlConnection(factory.Database.SuperuserConnectionString);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand("select count(*) from platform.tenants where slug = @s", db);
        cmd.Parameters.AddWithValue("s", slug);
        Assert.Equal(1L, (long)(await cmd.ExecuteScalarAsync())!);

        await using var audit = new NpgsqlCommand("select count(*) from platform.platform_audit_events where action = 'platform.tenant.provisioned' and target_tenant_id = @t", db);
        audit.Parameters.AddWithValue("t", firstBody["tenantId"]!.GetValue<Guid>());
        Assert.Equal(1L, (long)(await audit.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Provisioning_requires_an_idempotency_key_and_a_platform_administrator()
    {
        var ops = await _api.CreatePlatformAdminAsync();
        var noKey = TestApi.ProvisionRequest("nokey" + Guid.NewGuid().ToString("N")[..6], "x@y.test", "x");
        noKey.Headers.Remove("Idempotency-Key");
        var res = await _api.As(ops.Token).SendAsync(noKey);
        Assert.Equal("idempotency_key_required", await TestApi.ErrorCode(res));

        var tenant = await _api.ProvisionTenantAsync(ops);
        var asTenant = await _api.As(tenant.Admin.Token).SendAsync(TestApi.ProvisionRequest("hijack" + Guid.NewGuid().ToString("N")[..6], "x@y.test", Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.Forbidden, asTenant.StatusCode);

        var anonymous = await _api.Anonymous().SendAsync(TestApi.ProvisionRequest("anon" + Guid.NewGuid().ToString("N")[..6], "x@y.test", Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Platform_console_lists_tenants_but_cannot_read_tenant_data()
    {
        var ops = await _api.CreatePlatformAdminAsync();
        var tenant = await _api.ProvisionTenantAsync(ops);
        var client = _api.As(ops.Token);

        var list = await TestApi.Body(await client.GetAsync($"/api/v1/platform/tenants?search={tenant.Slug}"));
        Assert.Equal(tenant.TenantId, list["items"]!.AsArray().Single()!["tenantId"]!.GetValue<Guid>());

        // Platform tokens are rejected by every tenant endpoint.
        foreach (var path in new[] { "/api/v1/org/legal-entities", "/api/v1/users", "/api/v1/audit-events", "/api/v1/me" })
        {
            var res = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        }
    }
}
