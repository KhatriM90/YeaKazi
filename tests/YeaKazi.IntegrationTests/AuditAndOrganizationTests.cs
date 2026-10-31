using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Npgsql;
using YeaKazi.IntegrationTests.Infrastructure;

namespace YeaKazi.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuditAndOrganizationTests(ApiFactory factory) : IAsyncLifetime
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

    private async Task<long> AuditCountAsync()
    {
        await using var db = new NpgsqlConnection(factory.Database.SuperuserConnectionString);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand("select count(*) from audit.audit_events where tenant_id = @t", db);
        cmd.Parameters.AddWithValue("t", _t.TenantId);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<JsonObject> WriteAndExpectAudit(Func<Task<HttpResponseMessage>> write, string expectedAction)
    {
        var before = await AuditCountAsync();
        var res = await write();
        var body = await TestApi.Body(res);
        Assert.True(await AuditCountAsync() > before, $"No audit event for {expectedAction}");
        var latest = await TestApi.Body(await Admin.GetAsync($"/api/v1/audit-events?action={expectedAction}&pageSize=1"));
        Assert.NotEmpty(latest["items"]!.AsArray());
        return body;
    }

    [Fact]
    public async Task Every_organization_write_creates_an_audit_event()
    {
        var le = _t.LegalEntityId;
        var cc = await WriteAndExpectAudit(() => Admin.PostAsJsonAsync("/api/v1/org/cost-centers", new { legalEntityId = le, code = "CC1", name = "Head office" }), "org.cost_center.created");
        var grade = await WriteAndExpectAudit(() => Admin.PostAsJsonAsync("/api/v1/org/grades", new { legalEntityId = le, code = "G1", name = "Grade 1", rank = 1 }), "org.grade.created");
        var branch = await WriteAndExpectAudit(() => Admin.PostAsJsonAsync("/api/v1/org/branches", new { legalEntityId = le, code = "NBO", name = "Nairobi", address = "Upper Hill" }), "org.branch.created");
        var dept = await WriteAndExpectAudit(() => Admin.PostAsJsonAsync("/api/v1/org/departments", new { legalEntityId = le, code = "OPS", name = "Operations", costCenterId = cc["id"]!.GetValue<Guid>() }), "org.department.created");
        await WriteAndExpectAudit(() => Admin.PostAsJsonAsync("/api/v1/org/positions", new
        {
            legalEntityId = le, code = "OPSMGR", name = "Operations manager",
            departmentId = dept["id"]!.GetValue<Guid>(), gradeId = grade["id"]!.GetValue<Guid>(), branchId = branch["id"]!.GetValue<Guid>(),
        }), "org.position.created");
        var cal = await WriteAndExpectAudit(() => Admin.PostAsJsonAsync("/api/v1/org/holiday-calendars", new { legalEntityId = le, code = "KE", name = "Kenya holidays" }), "org.holiday_calendar.created");
        var holiday = await WriteAndExpectAudit(() => Admin.PostAsJsonAsync($"/api/v1/org/holiday-calendars/{cal["id"]}/holidays", new { date = "2026-12-31", name = "Company day", kind = "company" }), "org.holiday.created");
        await WriteAndExpectAudit(() => Admin.PutAsJsonAsync($"/api/v1/org/holiday-calendars/{cal["id"]}/holidays/{holiday["id"]}", new { name = "Company day", kind = "company", isActive = false, version = holiday["version"]!.GetValue<uint>(), reason = "Cancelled" }), "org.holiday.deactivated");
        var ws = await WriteAndExpectAudit(() => Admin.PostAsJsonAsync("/api/v1/org/work-schedules", new { legalEntityId = le, code = "NIGHT", name = "Night shift", startTime = "22:00", endTime = "06:00", breakMinutes = 30, workingDays = new[] { 1, 2, 3, 4, 5 } }), "org.work_schedule.created");
        Assert.True(ws["isOvernight"]!.GetValue<bool>());
        Assert.Equal(450, ws["scheduledMinutesPerDay"]!.GetValue<int>());

        var updated = await WriteAndExpectAudit(() => Admin.PutAsJsonAsync($"/api/v1/org/branches/{branch["id"]}", new { name = "Nairobi HQ", address = "Upper Hill", isActive = true, version = branch["version"]!.GetValue<uint>(), reason = "Rename" }), "org.branch.updated");
        Assert.Equal("Nairobi HQ", updated["name"]!.GetValue<string>());

        var leDto = await TestApi.Body(await Admin.GetAsync($"/api/v1/org/legal-entities/{le}"));
        await WriteAndExpectAudit(() => Admin.PutAsJsonAsync($"/api/v1/org/legal-entities/{le}", new { name = "Renamed Ltd", kraPin = "P000000001A", timezone = "Africa/Nairobi", isActive = true, version = leDto["version"]!.GetValue<uint>() }), "org.legal_entity.updated");

        // The audit event carries before/after values and the reason.
        var evt = (await TestApi.Body(await Admin.GetAsync("/api/v1/audit-events?action=org.branch.updated")))["items"]!.AsArray().First()!;
        Assert.Equal("Nairobi", evt["before"]!["name"]!.GetValue<string>());
        Assert.Equal("Nairobi HQ", evt["after"]!["name"]!.GetValue<string>());
        Assert.Equal("Rename", evt["reason"]!.GetValue<string>());
        Assert.Equal(_t.AdminUserId, evt["actorUserId"]!.GetValue<Guid>());
        Assert.False(string.IsNullOrEmpty(evt["correlationId"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Holiday_changes_publish_an_outbox_event()
    {
        var cal = await TestApi.Body(await Admin.PostAsJsonAsync("/api/v1/org/holiday-calendars", new { legalEntityId = _t.LegalEntityId, code = "H2", name = "Cal" }));
        await TestApi.EnsureSuccess(await Admin.PostAsJsonAsync($"/api/v1/org/holiday-calendars/{cal["id"]}/holidays", new { date = "2026-11-20", name = "Short notice day", kind = "short_notice" }));
        await using var db = new NpgsqlConnection(factory.Database.SuperuserConnectionString);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand("select count(*) from messaging.outbox_messages where tenant_id = @t and type = 'org.holiday_changed'", db);
        cmd.Parameters.AddWithValue("t", _t.TenantId);
        Assert.True((long)(await cmd.ExecuteScalarAsync())! >= 1);
    }

    [Fact]
    public async Task Circular_reporting_lines_and_department_cycles_are_rejected()
    {
        var le = _t.LegalEntityId;
        async Task<JsonObject> Position(string code, Guid? reportsTo) => await TestApi.Body(await Admin.PostAsJsonAsync("/api/v1/org/positions",
            new { legalEntityId = le, code, name = code, reportsToPositionId = reportsTo }));
        var ceo = await Position("CEO", null);
        var cfo = await Position("CFO", ceo["id"]!.GetValue<Guid>());
        var acc = await Position("ACC", cfo["id"]!.GetValue<Guid>());

        var loop = await Admin.PutAsJsonAsync($"/api/v1/org/positions/{ceo["id"]}", new
        {
            name = "CEO", reportsToPositionId = acc["id"]!.GetValue<Guid>(), isActive = true, version = ceo["version"]!.GetValue<uint>(),
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, loop.StatusCode);
        Assert.Equal("circular_reporting_line", await TestApi.ErrorCode(loop));

        var selfLoop = await Admin.PutAsJsonAsync($"/api/v1/org/positions/{cfo["id"]}", new
        {
            name = "CFO", reportsToPositionId = cfo["id"]!.GetValue<Guid>(), isActive = true, version = cfo["version"]!.GetValue<uint>(),
        });
        Assert.Equal("circular_reporting_line", await TestApi.ErrorCode(selfLoop));

        var parent = await TestApi.Body(await Admin.PostAsJsonAsync("/api/v1/org/departments", new { legalEntityId = le, code = "D1", name = "D1" }));
        var child = await TestApi.Body(await Admin.PostAsJsonAsync("/api/v1/org/departments", new { legalEntityId = le, code = "D2", name = "D2", parentDepartmentId = parent["id"]!.GetValue<Guid>() }));
        var deptLoop = await Admin.PutAsJsonAsync($"/api/v1/org/departments/{parent["id"]}", new
        {
            name = "D1", parentDepartmentId = child["id"]!.GetValue<Guid>(), isActive = true, version = parent["version"]!.GetValue<uint>(),
        });
        Assert.Equal("circular_hierarchy", await TestApi.ErrorCode(deptLoop));

        // A manager position with active reports cannot be deactivated.
        var deactivate = await Admin.PutAsJsonAsync($"/api/v1/org/positions/{cfo["id"]}", new { name = "CFO", reportsToPositionId = ceo["id"]!.GetValue<Guid>(), isActive = false, version = cfo["version"]!.GetValue<uint>() });
        Assert.Equal("in_use", await TestApi.ErrorCode(deactivate));
    }

    [Fact]
    public async Task Stale_version_is_rejected_with_a_concurrency_conflict()
    {
        var branch = await TestApi.Body(await Admin.PostAsJsonAsync("/api/v1/org/branches", new { legalEntityId = _t.LegalEntityId, code = "CON", name = "Concurrency" }));
        var version = branch["version"]!.GetValue<uint>();
        await TestApi.EnsureSuccess(await Admin.PutAsJsonAsync($"/api/v1/org/branches/{branch["id"]}", new { name = "First", isActive = true, version }));
        var stale = await Admin.PutAsJsonAsync($"/api/v1/org/branches/{branch["id"]}", new { name = "Second", isActive = true, version });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("concurrency_conflict", await TestApi.ErrorCode(stale));
    }

    [Fact]
    public async Task Validation_errors_are_reported_per_field_and_duplicates_conflict()
    {
        var bad = await Admin.PostAsJsonAsync("/api/v1/org/legal-entities", new { code = "", name = "", kraPin = "12345", timezone = "Mars/Olympus" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var errors = (await bad.Content.ReadFromJsonAsync<JsonObject>())!["errors"]!.AsObject();
        Assert.True(errors.ContainsKey("code"));
        Assert.True(errors.ContainsKey("name"));
        Assert.True(errors.ContainsKey("kraPin"));
        Assert.True(errors.ContainsKey("timezone"));
        Assert.True(bad.Headers.Contains("X-Correlation-Id"));

        await TestApi.EnsureSuccess(await Admin.PostAsJsonAsync("/api/v1/org/grades", new { legalEntityId = _t.LegalEntityId, code = "DUP", name = "Dup", rank = 2 }));
        var dup = await Admin.PostAsJsonAsync("/api/v1/org/grades", new { legalEntityId = _t.LegalEntityId, code = "dup", name = "Dup again", rank = 3 });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [Fact]
    public async Task Audit_log_is_append_only_even_for_the_owner_and_the_hash_chain_verifies()
    {
        var verify = await TestApi.Body(await Admin.PostAsync("/api/v1/audit-events/verify-chain", null));
        Assert.True(verify["valid"]!.GetValue<bool>());
        Assert.True(verify["eventsChecked"]!.GetValue<long>() > 5);

        foreach (var cs in new[] { factory.Database.AppConnectionString, factory.Database.OwnerConnectionString })
        {
            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync();
            await using (var set = new NpgsqlCommand($"select set_config('app.tenant_id', '{_t.TenantId}', false)", conn))
            {
                await set.ExecuteNonQueryAsync();
            }

            await using var update = new NpgsqlCommand("update audit.audit_events set action = 'tampered'", conn);
            var ex = await Assert.ThrowsAsync<PostgresException>(() => update.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
            await using var delete = new NpgsqlCommand("delete from audit.audit_events", conn);
            ex = await Assert.ThrowsAsync<PostgresException>(() => delete.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
        }
    }

    [Fact]
    public async Task Tampering_by_a_superuser_is_detected_by_the_hash_chain()
    {
        var ops = await _api.CreatePlatformAdminAsync();
        var victim = await _api.ProvisionTenantAsync(ops);
        await using (var conn = new NpgsqlConnection(factory.Database.SuperuserConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("""
                alter table audit.audit_events disable trigger audit_events_append_only;
                update audit.audit_events set reason = 'rewritten' where tenant_id = @t and seq = 2;
                alter table audit.audit_events enable trigger audit_events_append_only;
                """, conn);
            cmd.Parameters.AddWithValue("t", victim.TenantId);
            await cmd.ExecuteNonQueryAsync();
        }

        var verify = await TestApi.Body(await _api.As(victim.Admin.Token).PostAsync("/api/v1/audit-events/verify-chain", null));
        Assert.False(verify["valid"]!.GetValue<bool>());
        Assert.Equal(2, verify["firstBrokenSeq"]!.GetValue<long>());
    }

    [Fact]
    public async Task Searching_the_audit_log_is_itself_audited()
    {
        await TestApi.EnsureSuccess(await Admin.GetAsync("/api/v1/audit-events?entityType=branch"));
        var res = await TestApi.Body(await Admin.GetAsync("/api/v1/audit-events?action=audit.searched"));
        Assert.NotEmpty(res["items"]!.AsArray());
    }

    [Fact]
    public async Task Invitation_email_has_an_activation_link_and_the_outbox_row_is_processed()
    {
        var email = $"inv{Guid.NewGuid():N}@x.test";
        var role = await _api.RoleIdAsync(_t.Admin.Token, "employee");
        await TestApi.EnsureSuccess(await Admin.PostAsJsonAsync("/api/v1/users", new { email, displayName = "Invitee", roles = new[] { new { roleId = role } } }));
        await factory.DispatchOutboxAsync();
        var mail = factory.Email.Sent.Last(m => m.To == email);
        Assert.Contains("https://portal.test/activate?tenant=", mail.TextBody);

        await using var db = new NpgsqlConnection(factory.Database.SuperuserConnectionString);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand("select count(*) from messaging.outbox_messages where tenant_id = @t and processed_at is null", db);
        cmd.Parameters.AddWithValue("t", _t.TenantId);
        Assert.Equal(0L, (long)(await cmd.ExecuteScalarAsync())!);
        // The token is never stored in clear text in the outbox payload.
        await using var payload = new NpgsqlCommand("select payload::text from messaging.outbox_messages where tenant_id = @t and type = 'iam.user_invited' order by occurred_at desc limit 1", db);
        payload.Parameters.AddWithValue("t", _t.TenantId);
        var text = (string)(await payload.ExecuteScalarAsync())!;
        var token = System.Web.HttpUtility.ParseQueryString(new Uri(mail.TextBody.Split('\n').Select(l => l.Trim()).First(l => l.StartsWith("https://"))).Query)["token"]!;
        Assert.DoesNotContain(token, text);
    }
}
