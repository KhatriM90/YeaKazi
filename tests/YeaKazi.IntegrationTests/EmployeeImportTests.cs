using System.Net;
using System.Net.Http.Json;
using YeaKazi.IntegrationTests.Infrastructure;

namespace YeaKazi.IntegrationTests;

/// <summary>MIG-01 (employees): the S2 acceptance check. 200 sample employees import with row-level errors, then commit by a second user.</summary>
[Collection(ApiCollection.Name)]
public class EmployeeImportTests(ApiFactory factory) : IAsyncLifetime
{
    private readonly TestApi _api = new(factory);
    private TenantHandle _t = null!;
    private SignedIn _uploader = null!;
    private SignedIn _reviewer = null!;

    public async Task InitializeAsync()
    {
        var ops = await _api.CreatePlatformAdminAsync();
        _t = await _api.ProvisionTenantAsync(ops);
        var admin = _api.As(_t.Admin.Token);
        foreach (var code in new[] { "FIN", "OPS", "HR" })
        {
            await TestApi.EnsureSuccess(await admin.PostAsJsonAsync("/api/v1/org/departments", new { legalEntityId = _t.LegalEntityId, code, name = code }));
        }

        await TestApi.EnsureSuccess(await admin.PostAsJsonAsync("/api/v1/org/branches", new { legalEntityId = _t.LegalEntityId, code = "NBO", name = "Nairobi" }));
        var hr = await _api.RoleIdAsync(_t.Admin.Token, "hr_officer");
        await _api.InviteAndActivateAsync(_t, $"up@{_t.Slug}.test", (hr, null));
        await _api.InviteAndActivateAsync(_t, $"rev@{_t.Slug}.test", (hr, null));
        _uploader = await _api.SignInTenantAsync(_t.Slug, $"up@{_t.Slug}.test");
        _reviewer = await _api.SignInTenantAsync(_t.Slug, $"rev@{_t.Slug}.test");
        await TestApi.EnsureSuccess(await _api.As(_uploader.Token).PostAsJsonAsync("/api/v1/people/bank-branches",
            new { bankCode = "01", bankName = "Test Bank", branchCode = "001", branchName = "Head Office" }));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(TestDatabase.RepoRoot(), "tests", "fixtures", "people", name));

    [Fact]
    public async Task Two_hundred_employees_import_with_row_errors_then_commit_by_a_second_user()
    {
        var up = _api.As(_uploader.Token);
        var rev = _api.As(_reviewer.Token);

        // 1. The file with mistakes: every broken row is reported against its column, nothing is written.
        var bad = await TestApi.Body(await up.PostAsJsonAsync("/api/v1/people/imports",
            new { legalEntityId = _t.LegalEntityId, fileName = "employees_200_errors.csv", content = Fixture("employees_200_errors.csv") }));
        Assert.Equal(200, bad["totalRows"]!.GetValue<int>());
        Assert.True(bad["errorRows"]!.GetValue<int>() >= 12);
        Assert.False(bad["canCommit"]!.GetValue<bool>());
        var rows = await TestApi.Body(await up.GetAsync($"/api/v1/people/imports/{bad["id"]}/rows?onlyErrors=true&pageSize=200"));
        var reported = rows["items"]!.AsArray()
            .SelectMany(r => r!["errors"]!.AsArray().Select(e => (Row: r["rowNumber"]!.GetValue<int>(), Field: e!["field"]!.GetValue<string>())))
            .ToHashSet();
        foreach (var line in Fixture("employees_200_errors.expected.txt").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(',');
            Assert.Contains((int.Parse(parts[0]), parts[1]), reported);
        }

        var badCommit = await rev.PostAsync($"/api/v1/people/imports/{bad["id"]}/commit", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badCommit.StatusCode);
        Assert.Equal("has_errors", await TestApi.ErrorCode(badCommit));
        Assert.Equal(0, (await TestApi.Body(await up.GetAsync("/api/v1/people/employees")))["total"]!.GetValue<int>());
        await TestApi.EnsureSuccess(await up.PostAsync($"/api/v1/people/imports/{bad["id"]}/discard", null));

        // 2. The corrected file validates cleanly but the uploader cannot commit it.
        var good = await TestApi.Body(await up.PostAsJsonAsync("/api/v1/people/imports",
            new { legalEntityId = _t.LegalEntityId, fileName = "employees_200.csv", content = Fixture("employees_200.csv") }));
        Assert.Equal(0, good["errorRows"]!.GetValue<int>());
        Assert.False(good["canCommit"]!.GetValue<bool>());
        var self = await up.PostAsync($"/api/v1/people/imports/{good["id"]}/commit", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, self.StatusCode);
        Assert.Equal("self_approval", await TestApi.ErrorCode(self));

        // 3. A second HR user reviews and commits all 200 in one transaction; a repeat commit is a no-op.
        var preview = await TestApi.Body(await rev.GetAsync($"/api/v1/people/imports/{good["id"]}"));
        Assert.True(preview["canCommit"]!.GetValue<bool>());
        var committed = await TestApi.Body(await rev.PostAsync($"/api/v1/people/imports/{good["id"]}/commit", null));
        Assert.Equal("committed", committed["status"]!.GetValue<string>());
        var again = await TestApi.Body(await rev.PostAsync($"/api/v1/people/imports/{good["id"]}/commit", null));
        Assert.Equal("committed", again["status"]!.GetValue<string>());

        var list = await TestApi.Body(await up.GetAsync("/api/v1/people/employees?pageSize=200"));
        Assert.Equal(200, list["total"]!.GetValue<int>());

        // In-file managers resolved, payment details approved by the committer, hire history written.
        var e2 = list["items"]!.AsArray().Single(x => x!["employeeNumber"]!.GetValue<string>() == "E0002")!;
        var detail = await TestApi.Body(await up.GetAsync($"/api/v1/people/employees/{e2["id"]}"));
        Assert.Equal("Wanjiru Mutiso (E0001)", detail["currentAssignment"]!["managerName"]!.GetValue<string>());
        Assert.Equal("bank", detail["paymentDetails"]!["paymentMethod"]!.GetValue<string>());
        Assert.Null(detail["pendingPaymentChange"]);
        Assert.Equal(good["id"]!.GetValue<Guid>(), detail["importBatchId"]!.GetValue<Guid>());

        // History is preserved on change after import.
        var history = await TestApi.Body(await up.PostAsJsonAsync($"/api/v1/people/employees/{e2["id"]}/assignments", new
        {
            effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"), changeType = "change", reason = "Retitle",
            assignment = new { workerCategory = "permanent", payBasis = "monthly", jobTitle = "Senior Officer" },
        }));
        var versions = history["episodes"]![0]!["versions"]!.AsArray();
        Assert.Equal(2, versions.Count);
        Assert.Equal("hire", versions[0]!["changeType"]!.GetValue<string>());
        Assert.Equal("Officer", versions[0]!["jobTitle"]!.GetValue<string>());

        var audit = await TestApi.Body(await _api.As(_t.Admin.Token).GetAsync("/api/v1/audit-events?action=people.import.committed"));
        Assert.Single(audit["items"]!.AsArray());
    }

    [Fact]
    public async Task Template_lists_every_column_and_bad_headers_are_rejected()
    {
        var up = _api.As(_uploader.Token);
        var template = await (await up.GetAsync("/api/v1/people/imports/template")).Content.ReadAsStringAsync();
        Assert.StartsWith("employee_number,hire_date,first_name", template);
        var res = await up.PostAsJsonAsync("/api/v1/people/imports", new { legalEntityId = _t.LegalEntityId, fileName = "x.csv", content = "name,salary\nA,1\n" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
