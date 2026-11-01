using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Npgsql;
using YeaKazi.IntegrationTests.Infrastructure;

namespace YeaKazi.IntegrationTests;

/// <summary>S2: employee master, employment history, payment four-eyes approval and masking.</summary>
[Collection(ApiCollection.Name)]
public class PeopleTests(ApiFactory factory) : IAsyncLifetime
{
    private readonly TestApi _api = new(factory);
    private TenantHandle _t = null!;
    private SignedIn _hr1 = null!;
    private SignedIn _hr2 = null!;
    private SignedIn _preparer = null!;
    private Guid _fin;
    private Guid _ops;
    private Guid _nbo;

    public async Task InitializeAsync()
    {
        var ops = await _api.CreatePlatformAdminAsync();
        _t = await _api.ProvisionTenantAsync(ops);
        var admin = _api.As(_t.Admin.Token);
        _fin = await Id(admin.PostAsJsonAsync("/api/v1/org/departments", new { legalEntityId = _t.LegalEntityId, code = "FIN", name = "Finance" }));
        _ops = await Id(admin.PostAsJsonAsync("/api/v1/org/departments", new { legalEntityId = _t.LegalEntityId, code = "OPS", name = "Operations" }));
        _nbo = await Id(admin.PostAsJsonAsync("/api/v1/org/branches", new { legalEntityId = _t.LegalEntityId, code = "NBO", name = "Nairobi" }));

        var hrRole = await _api.RoleIdAsync(_t.Admin.Token, "hr_officer");
        var preparerRole = await _api.RoleIdAsync(_t.Admin.Token, "payroll_preparer");
        await _api.InviteAndActivateAsync(_t, $"hr1@{_t.Slug}.test", (hrRole, null));
        await _api.InviteAndActivateAsync(_t, $"hr2@{_t.Slug}.test", (hrRole, null));
        await _api.InviteAndActivateAsync(_t, $"prep@{_t.Slug}.test", (preparerRole, null));
        _hr1 = await _api.SignInTenantAsync(_t.Slug, $"hr1@{_t.Slug}.test");
        _hr2 = await _api.SignInTenantAsync(_t.Slug, $"hr2@{_t.Slug}.test");
        _preparer = await _api.SignInTenantAsync(_t.Slug, $"prep@{_t.Slug}.test");
        await TestApi.EnsureSuccess(await _api.As(_hr1.Token).PostAsJsonAsync("/api/v1/people/bank-branches",
            new { bankCode = "01", bankName = "Test Bank", branchCode = "001", branchName = "Head Office" }));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<Guid> Id(Task<HttpResponseMessage> call) => (await TestApi.Body(await call))["id"]!.GetValue<Guid>();

    private object NewEmployee(string number, string nationalId, string kraPin, string hireDate = "2025-01-06") => new
    {
        legalEntityId = _t.LegalEntityId,
        employeeNumber = number,
        hireDate,
        personal = new
        {
            firstName = "Achieng", lastName = "Odhiambo", dateOfBirth = "1990-04-12", gender = "female", citizenship = "kenyan",
            residencyStatus = "resident", phoneMobile = "0712345678", emailWork = $"{number.ToLowerInvariant()}@example.co.ke",
        },
        identifiers = new { nationalId, kraPin, nssfNo = "123456", shaNo = "SHA123" },
        emergencyContacts = new[] { new { name = "Otieno Odhiambo", relationship = "spouse", phone = "+254722000111" } },
        assignment = new { workerCategory = "permanent", payBasis = "monthly", jobTitle = "Accountant", departmentId = _fin, branchId = _nbo },
        paymentDetails = new { paymentMethod = "bank", bankCode = "01", bankBranchCode = "001", accountNo = "1100223344", accountName = "Achieng Odhiambo" },
    };

    private static object Assignment(string title, Guid department, Guid branch) =>
        new { workerCategory = "permanent", payBasis = "monthly", jobTitle = title, departmentId = department, branchId = branch };

    [Fact]
    public async Task Hire_promote_transfer_terminate_and_rehire_preserve_history()
    {
        var hr = _api.As(_hr1.Token);
        var created = await TestApi.Body(await hr.PostAsJsonAsync("/api/v1/people/employees", NewEmployee("E100", "30100100", "A301001001Z")));
        var id = created["id"]!.GetValue<Guid>();
        Assert.Equal("active", created["status"]!.GetValue<string>());
        Assert.Equal("hire", created["currentAssignment"]!["changeType"]!.GetValue<string>());

        // Promotion in the past, transfer in the future.
        var future = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(40).ToString("yyyy-MM-dd");
        await TestApi.EnsureSuccess(await hr.PostAsJsonAsync($"/api/v1/people/employees/{id}/assignments",
            new { effectiveFrom = "2025-06-01", changeType = "promotion", assignment = Assignment("Senior Accountant", _fin, _nbo), reason = "Annual review" }));
        var afterTransfer = await TestApi.Body(await hr.PostAsJsonAsync($"/api/v1/people/employees/{id}/assignments",
            new { effectiveFrom = future, changeType = "transfer", assignment = Assignment("Senior Accountant", _ops, _nbo), reason = "Restructure" }));
        var versions = afterTransfer["episodes"]![0]!["versions"]!.AsArray();
        Assert.Equal(3, versions.Count);
        Assert.Equal("2025-05-31", versions[0]!["effectiveTo"]!.GetValue<string>());

        // Current view stays on the promotion until the transfer date.
        var now = await TestApi.Body(await hr.GetAsync($"/api/v1/people/employees/{id}"));
        Assert.Equal(_fin, now["currentAssignment"]!["departmentId"]!.GetValue<Guid>());

        // A wrong promotion is corrected: the old version is kept, marked cancelled, and a new one replaces it.
        var promotion = versions[1]!;
        var corrected = await TestApi.Body(await hr.PostAsJsonAsync($"/api/v1/people/employees/{id}/assignments/{promotion["id"]}/correct",
            new { assignment = Assignment("Senior Accountant II", _fin, _nbo), reason = "Typo in title", version = promotion["version"]!.GetValue<uint>() }));
        var all = corrected["episodes"]![0]!["versions"]!.AsArray();
        Assert.Contains(all, v => v!["isCancelled"]!.GetValue<bool>() && v["jobTitle"]!.GetValue<string>() == "Senior Accountant");
        Assert.Contains(all, v => !v!["isCancelled"]!.GetValue<bool>() && v["correctsVersionId"]?.GetValue<Guid>() == promotion["id"]!.GetValue<Guid>());

        // A planned change blocks a termination before it; cancel it first.
        var episode = corrected["episodes"]![0]!["episode"]!;
        var blocked = await hr.PostAsJsonAsync($"/api/v1/people/employees/{id}/terminate",
            new { terminationDate = "2026-01-31", terminationType = "resignation", reason = "Moved abroad", version = episode["version"]!.GetValue<uint>() });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);
        Assert.Equal("later_changes_exist", await TestApi.ErrorCode(blocked));
        var transfer = all.Single(v => v!["changeType"]!.GetValue<string>() == "transfer")!;
        await TestApi.EnsureSuccess(await hr.PostAsJsonAsync($"/api/v1/people/employees/{id}/assignments/{transfer["id"]}/cancel",
            new { reason = "Restructure postponed", version = transfer["version"]!.GetValue<uint>() }));

        var terminated = await TestApi.Body(await hr.PostAsJsonAsync($"/api/v1/people/employees/{id}/terminate",
            new { terminationDate = "2026-01-31", terminationType = "resignation", reason = "Moved abroad", version = episode["version"]!.GetValue<uint>() }));
        Assert.Equal("2026-01-31", terminated["episodes"]![0]!["episode"]!["terminationDate"]!.GetValue<string>());
        Assert.Equal("terminated", (await TestApi.Body(await hr.GetAsync($"/api/v1/people/employees/{id}")))["status"]!.GetValue<string>());

        // Rehire opens a second episode; the first keeps all its versions.
        var rehired = await TestApi.Body(await hr.PostAsJsonAsync($"/api/v1/people/employees/{id}/rehire",
            new { hireDate = "2026-03-02", assignment = Assignment("Finance Lead", _fin, _nbo), reason = "Returned" }));
        var episodes = rehired["episodes"]!.AsArray();
        Assert.Equal(2, episodes.Count);
        Assert.Equal(4, episodes[0]!["versions"]!.AsArray().Count);
        Assert.Equal("rehire", episodes[1]!["versions"]![0]!["changeType"]!.GetValue<string>());
        Assert.Equal("active", (await TestApi.Body(await hr.GetAsync($"/api/v1/people/employees/{id}")))["status"]!.GetValue<string>());

        // Every step is audited.
        foreach (var action in new[] { "people.employee.hired", "people.assignment.promotion", "people.assignment.transfer", "people.assignment.corrected", "people.assignment.cancelled", "people.employee.terminated", "people.employee.rehired" })
        {
            var events = await TestApi.Body(await _api.As(_t.Admin.Token).GetAsync($"/api/v1/audit-events?action={action}"));
            Assert.True(events["items"]!.AsArray().Count >= 1, $"Missing audit {action}");
        }

        // The database refuses to rewrite history, even for the schema owner.
        await using var db = new NpgsqlConnection(factory.Database.SuperuserConnectionString);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand("update people.assignment_versions set job_title = 'Rewritten' where employee_id = @id", db);
        cmd.Parameters.AddWithValue("id", id);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal("23514", ex.SqlState);
    }

    [Fact]
    public async Task Payment_details_need_a_second_person()
    {
        var hr1 = _api.As(_hr1.Token);
        var created = await TestApi.Body(await hr1.PostAsJsonAsync("/api/v1/people/employees", NewEmployee("E200", "30200200", "A302002002Z")));
        var id = created["id"]!.GetValue<Guid>();
        Assert.Null(created["paymentDetails"]);
        var pending = created["pendingPaymentChange"]!;
        Assert.Contains("payment_details_not_approved", created["statutoryGaps"]!.AsArray().Select(g => g!.GetValue<string>()));

        var self = await hr1.PostAsJsonAsync($"/api/v1/people/payment-changes/{pending["id"]}/approve", new { version = pending["version"]!.GetValue<uint>() });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, self.StatusCode);
        Assert.Equal("self_approval", await TestApi.ErrorCode(self));

        var approved = await TestApi.Body(await _api.As(_hr2.Token).PostAsJsonAsync($"/api/v1/people/payment-changes/{pending["id"]}/approve",
            new { note = "Checked against bank letter", version = pending["version"]!.GetValue<uint>() }));
        Assert.Equal("approved", approved["status"]!.GetValue<string>());
        var after = await TestApi.Body(await hr1.GetAsync($"/api/v1/people/employees/{id}"));
        Assert.Equal("bank", after["paymentDetails"]!["paymentMethod"]!.GetValue<string>());

        // A second change while one is pending is refused.
        var change = new { details = new { paymentMethod = "mpesa", mpesaPhone = "0722000222" }, reason = "Prefers M-Pesa" };
        await TestApi.EnsureSuccess(await hr1.PostAsJsonAsync($"/api/v1/people/employees/{id}/payment-changes", change));
        var second = await hr1.PostAsJsonAsync($"/api/v1/people/employees/{id}/payment-changes", change);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Identifiers_are_masked_without_sensitive_permission_and_unique_per_employer()
    {
        var hr = _api.As(_hr1.Token);
        var id = await Id(hr.PostAsJsonAsync("/api/v1/people/employees", NewEmployee("E300", "30300300", "A303003003Z")));

        var full = await TestApi.Body(await hr.GetAsync($"/api/v1/people/employees/{id}"));
        Assert.False(full["sensitiveMasked"]!.GetValue<bool>());
        Assert.Equal("A303003003Z", full["identifiers"]!["kraPin"]!.GetValue<string>());

        var masked = await TestApi.Body(await _api.As(_preparer.Token).GetAsync($"/api/v1/people/employees/{id}"));
        Assert.True(masked["sensitiveMasked"]!.GetValue<bool>());
        Assert.NotEqual("A303003003Z", masked["identifiers"]!["kraPin"]!.GetValue<string>());
        Assert.DoesNotContain("30300300", masked.ToJsonString());

        // Read-only users cannot hire.
        var denied = await _api.As(_preparer.Token).PostAsJsonAsync("/api/v1/people/employees", NewEmployee("E301", "30300301", "A303003013Z"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        // Tenant admin has no people permissions by default (least privilege).
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.As(_t.Admin.Token).GetAsync("/api/v1/people/employees")).StatusCode);

        // Same national ID or KRA PIN in the same employer is rejected.
        var dup = await hr.PostAsJsonAsync("/api/v1/people/employees", NewEmployee("E302", "30300300", "A303003003Z"));
        Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode);
        var body = await TestApi.Body(await hr.GetAsync("/api/v1/people/employees?search=E30"));
        Assert.Single(body["items"]!.AsArray());
    }

    [Fact]
    public async Task Employees_are_isolated_between_tenants()
    {
        var id = await Id(_api.As(_hr1.Token).PostAsJsonAsync("/api/v1/people/employees", NewEmployee("E400", "30400400", "A304004004Z")));
        var ops = await _api.CreatePlatformAdminAsync();
        var other = await _api.ProvisionTenantAsync(ops);
        var hrRole = await _api.RoleIdAsync(other.Admin.Token, "hr_officer");
        await _api.InviteAndActivateAsync(other, $"hr@{other.Slug}.test", (hrRole, null));
        var otherHr = _api.As((await _api.SignInTenantAsync(other.Slug, $"hr@{other.Slug}.test")).Token);

        Assert.Equal(HttpStatusCode.NotFound, (await otherHr.GetAsync($"/api/v1/people/employees/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherHr.GetAsync($"/api/v1/people/employees/{id}/history")).StatusCode);
        var list = await TestApi.Body(await otherHr.GetAsync("/api/v1/people/employees"));
        Assert.Empty(list["items"]!.AsArray());
        // Hiring into another tenant's legal entity fails.
        var cross = await otherHr.PostAsJsonAsync("/api/v1/people/employees", NewEmployee("E401", "30400401", "A304004014Z"));
        Assert.True(TestApi.IsDenied(cross.StatusCode));
        // Same identifiers are allowed in a different tenant (uniqueness is per employer).
        var own = NewEmployee("E400", "30400400", "A304004004Z");
        var json = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(own, TestApi.Json))!.AsObject();
        json["legalEntityId"] = other.LegalEntityId;
        json["assignment"] = JsonNode.Parse("""{"workerCategory":"casual","payBasis":"daily"}""");
        json["paymentDetails"] = JsonNode.Parse("""{"paymentMethod":"cash"}""");
        await TestApi.EnsureSuccess(await otherHr.PostAsJsonAsync("/api/v1/people/employees", json));
    }
}
