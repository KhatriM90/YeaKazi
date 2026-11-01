using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Npgsql;
using YeaKazi.IntegrationTests.Infrastructure;

namespace YeaKazi.IntegrationTests;

/// <summary>Cross-tenant negative tests (Section 11 isolation): API level and database level.</summary>
[Collection(ApiCollection.Name)]
public class TenantIsolationTests(ApiFactory factory) : IAsyncLifetime
{
    private readonly TestApi _api = new(factory);
    private TenantHandle _a = null!;
    private TenantHandle _b = null!;
    private Guid _branchB;
    private Guid _departmentB;

    public async Task InitializeAsync()
    {
        var ops = await _api.CreatePlatformAdminAsync();
        _a = await _api.ProvisionTenantAsync(ops);
        _b = await _api.ProvisionTenantAsync(ops);
        var b = _api.As(_b.Admin.Token);
        _branchB = (await TestApi.Body(await b.PostAsJsonAsync("/api/v1/org/branches", new { legalEntityId = _b.LegalEntityId, code = "NBO", name = "Nairobi B" })))["id"]!.GetValue<Guid>();
        _departmentB = (await TestApi.Body(await b.PostAsJsonAsync("/api/v1/org/departments", new { legalEntityId = _b.LegalEntityId, code = "FIN", name = "Finance B" })))["id"]!.GetValue<Guid>();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Lists_only_show_own_tenant()
    {
        var a = _api.As(_a.Admin.Token);
        var les = await TestApi.Body(await a.GetAsync("/api/v1/org/legal-entities?includeInactive=true"));
        Assert.All(les["items"]!.AsArray(), le => Assert.Equal(_a.LegalEntityId, le!["id"]!.GetValue<Guid>()));

        var branches = await TestApi.Body(await a.GetAsync("/api/v1/org/branches?includeInactive=true"));
        Assert.DoesNotContain(branches["items"]!.AsArray(), x => x!["id"]!.GetValue<Guid>() == _branchB);

        var filtered = await TestApi.Body(await a.GetAsync($"/api/v1/org/branches?legalEntityId={_b.LegalEntityId}"));
        Assert.Empty(filtered["items"]!.AsArray());

        var users = await TestApi.Body(await a.GetAsync("/api/v1/users"));
        Assert.DoesNotContain(users["items"]!.AsArray(), u => u!["id"]!.GetValue<Guid>() == _b.AdminUserId);
    }

    [Fact]
    public async Task Cannot_read_or_change_another_tenants_records_by_id()
    {
        var a = _api.As(_a.Admin.Token);
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync($"/api/v1/org/legal-entities/{_b.LegalEntityId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync($"/api/v1/org/branches/{_branchB}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync($"/api/v1/org/departments/{_departmentB}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync($"/api/v1/users/{_b.AdminUserId}")).StatusCode);

        var put = await a.PutAsJsonAsync($"/api/v1/org/branches/{_branchB}", new { name = "Hijacked", isActive = false, version = 0u });
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        var putLe = await a.PutAsJsonAsync($"/api/v1/org/legal-entities/{_b.LegalEntityId}", new { name = "Hijacked", timezone = "Africa/Nairobi", isActive = false, version = 0u });
        Assert.Equal(HttpStatusCode.NotFound, putLe.StatusCode);

        var disable = await a.PostAsJsonAsync($"/api/v1/users/{_b.AdminUserId}/disable", new { version = 0u, reason = "x" });
        Assert.Equal(HttpStatusCode.NotFound, disable.StatusCode);

        var roleA = await _api.RoleIdAsync(_a.Admin.Token, "auditor");
        var assign = await a.PostAsJsonAsync($"/api/v1/users/{_b.AdminUserId}/roles", new { roleId = roleA });
        Assert.Equal(HttpStatusCode.NotFound, assign.StatusCode);

        var bBranch = await TestApi.Body(await _api.As(_b.Admin.Token).GetAsync($"/api/v1/org/branches/{_branchB}"));
        Assert.Equal("Nairobi B", bBranch["name"]!.GetValue<string>());
        Assert.True(bBranch["isActive"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Cannot_create_records_under_or_referencing_another_tenant()
    {
        var a = _api.As(_a.Admin.Token);
        var underB = await a.PostAsJsonAsync("/api/v1/org/branches", new { legalEntityId = _b.LegalEntityId, code = "X1", name = "Sneaky" });
        Assert.True(TestApi.IsDenied(underB.StatusCode), $"Got {underB.StatusCode}");

        var refB = await a.PostAsJsonAsync("/api/v1/org/departments", new { legalEntityId = _a.LegalEntityId, code = "X2", name = "Child of B", parentDepartmentId = _departmentB });
        Assert.True(TestApi.IsDenied(refB.StatusCode), $"Got {refB.StatusCode}");

        var bRole = await _api.RoleIdAsync(_b.Admin.Token, "auditor");
        var invite = await a.PostAsJsonAsync("/api/v1/users", new { email = $"x{Guid.NewGuid():N}@a.test", displayName = "X", roles = new[] { new { roleId = bRole } } });
        Assert.True(TestApi.IsDenied(invite.StatusCode), $"Got {invite.StatusCode}");

        var bBranches = await TestApi.Body(await _api.As(_b.Admin.Token).GetAsync("/api/v1/org/branches?includeInactive=true"));
        Assert.DoesNotContain(bBranches["items"]!.AsArray(), x => x!["code"]!.GetValue<string>() == "X1");
    }

    [Fact]
    public async Task Tenant_id_in_request_body_is_ignored()
    {
        var a = _api.As(_a.Admin.Token);
        var res = await a.PostAsJsonAsync("/api/v1/org/cost-centers", new { tenantId = _b.TenantId, legalEntityId = _a.LegalEntityId, code = "CC-BODY", name = "Body tenant" });
        var created = await TestApi.Body(res);

        await using var db = new NpgsqlConnection(factory.Database.SuperuserConnectionString);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand("select tenant_id from org.cost_centers where id = @id", db);
        cmd.Parameters.AddWithValue("id", created["id"]!.GetValue<Guid>());
        Assert.Equal(_a.TenantId, (Guid)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Audit_search_never_returns_another_tenants_events()
    {
        var audit = await TestApi.Body(await _api.As(_a.Admin.Token).GetAsync("/api/v1/audit-events?pageSize=200"));
        var ids = audit["items"]!.AsArray().Select(e => e!["entityId"]?.GetValue<string>()).ToHashSet();
        Assert.DoesNotContain(_branchB.ToString(), ids);
        Assert.DoesNotContain(_b.LegalEntityId.ToString(), ids);
        Assert.DoesNotContain(_b.TenantId.ToString(), ids);
    }

    [Fact]
    public async Task Forged_tenant_claim_is_rejected()
    {
        var parts = _a.Admin.Token.Split('.');
        var payload = JsonNode.Parse(Base64UrlDecode(parts[1]))!.AsObject();
        payload["tid"] = _b.TenantId.ToString();
        var forged = $"{parts[0]}.{Base64UrlEncode(payload.ToJsonString())}.{parts[2]}";
        var res = await _api.As(forged).GetAsync("/api/v1/org/legal-entities");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Database_rls_hides_and_blocks_other_tenants_rows_for_the_app_role()
    {
        await using var conn = new NpgsqlConnection(factory.Database.AppConnectionString);
        await conn.OpenAsync();

        async Task<long> Count(string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            return (long)(await cmd.ExecuteScalarAsync())!;
        }

        async Task SetTenant(Guid? tenant, string scope)
        {
            await using var cmd = new NpgsqlCommand("select set_config('app.tenant_id', @t, false), set_config('app.scope', @s, false)", conn);
            cmd.Parameters.AddWithValue("t", tenant?.ToString() ?? string.Empty);
            cmd.Parameters.AddWithValue("s", scope);
            await cmd.ExecuteNonQueryAsync();
        }

        string[] tenantTables =
        [
            "org.legal_entities", "org.branches", "org.departments", "org.positions", "org.cost_centers", "org.grades",
            "org.holiday_calendars", "org.holidays", "org.work_schedules", "iam.users", "iam.roles", "iam.role_permissions",
            "iam.user_role_assignments", "iam.user_invitations", "iam.mfa_recovery_codes", "audit.audit_events",
            "people.employees", "people.emergency_contacts", "people.employment_episodes", "people.assignment_versions",
            "people.payment_detail_changes", "people.bank_branches", "people.import_batches", "people.import_rows",
        ];

        // No tenant set: nothing is visible.
        await SetTenant(null, string.Empty);
        foreach (var t in tenantTables)
        {
            Assert.Equal(0L, await Count($"select count(*) from {t}"));
        }

        // Platform scope sees the registry but no tenant data.
        await SetTenant(null, "platform");
        Assert.True(await Count("select count(*) from platform.tenants") >= 2);
        foreach (var t in tenantTables)
        {
            Assert.Equal(0L, await Count($"select count(*) from {t}"));
        }

        // Tenant A sees nothing of tenant B, even when asking for it explicitly.
        await SetTenant(_a.TenantId, "tenant");
        Assert.Equal(0L, await Count($"select count(*) from org.legal_entities where tenant_id = '{_b.TenantId}'"));
        Assert.Equal(0L, await Count($"select count(*) from org.branches where id = '{_branchB}'"));
        Assert.Equal(1L, await Count("select count(*) from platform.tenants"));
        Assert.Equal(0L, await Count("select count(*) from platform.platform_users"));

        // Writing a row for tenant B while in tenant A violates the policy.
        await using (var insert = new NpgsqlCommand(
            "insert into org.cost_centers (id, tenant_id, legal_entity_id, code, name, is_active, created_at) values (gen_random_uuid(), @t, @le, 'EVIL', 'Evil', true, now())", conn))
        {
            insert.Parameters.AddWithValue("t", _b.TenantId);
            insert.Parameters.AddWithValue("le", _b.LegalEntityId);
            var ex = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
        }

        // Updating another tenant's row affects nothing.
        await using (var update = new NpgsqlCommand($"update org.branches set name = 'x' where id = '{_branchB}'", conn))
        {
            Assert.Equal(0, await update.ExecuteNonQueryAsync());
        }

        // The app role cannot delete organization or people data at all.
        foreach (var table in new[] { "org.branches", "people.employees", "people.assignment_versions", "people.employment_episodes" })
        {
            await using var delete = new NpgsqlCommand($"delete from {table}", conn);
            var ex = await Assert.ThrowsAsync<PostgresException>(() => delete.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
        }
    }

    [Fact]
    public async Task Cross_tenant_composite_foreign_keys_hold_even_for_the_schema_owner()
    {
        await using var conn = new NpgsqlConnection(factory.Database.SuperuserConnectionString);
        await conn.OpenAsync();
        // Department of tenant A pointing at tenant B's legal entity must fail the composite FK.
        await using var cmd = new NpgsqlCommand(
            "insert into org.departments (id, tenant_id, legal_entity_id, code, name, is_active, created_at) values (gen_random_uuid(), @a, @leB, 'FK', 'FK', true, now())", conn);
        cmd.Parameters.AddWithValue("a", _a.TenantId);
        cmd.Parameters.AddWithValue("leB", _b.LegalEntityId);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ex.SqlState);
    }

    [Fact]
    public async Task Worker_role_reads_the_outbox_but_no_hr_data()
    {
        await using var conn = new NpgsqlConnection(factory.Database.WorkerConnectionString);
        await conn.OpenAsync();
        await using (var ok = new NpgsqlCommand("select count(*) from messaging.outbox_messages", conn))
        {
            Assert.True((long)(await ok.ExecuteScalarAsync())! > 0);
        }

        foreach (var table in new[] { "org.legal_entities", "iam.users", "audit.audit_events", "platform.platform_users", "people.employees", "people.import_rows", "rules.rule_versions" })
        {
            await using var denied = new NpgsqlCommand($"select count(*) from {table}", conn);
            var ex = await Assert.ThrowsAsync<PostgresException>(() => denied.ExecuteScalarAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
        }
    }

    private static string Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }

    private static string Base64UrlEncode(string s)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
