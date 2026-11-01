using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using YeaKazi.BuildingBlocks.Http;
using YeaKazi.People.Services;

namespace YeaKazi.People.Endpoints;

public static class PeopleEndpoints
{
    public static IEndpointRouteBuilder MapPeopleEndpoints(this IEndpointRouteBuilder app, string tenantPolicy)
    {
        var p = app.MapGroup("/api/v1/people").WithTags("People").RequireAuthorization(tenantPolicy);

        // Employees (EMP-01..04)
        p.MapGet("/employees", async Task<Ok<PagedResult<EmployeeSummaryDto>>> ([AsParameters] EmployeeListQuery q, EmployeeService s, CancellationToken ct)
            => TypedResults.Ok(await s.ListAsync(q, ct))).WithName("ListEmployees");
        p.MapGet("/employees/{id:guid}", async Task<Ok<EmployeeDto>> (Guid id, EmployeeService s, CancellationToken ct)
            => TypedResults.Ok(await s.GetAsync(id, ct))).WithName("GetEmployee");
        p.MapPost("/employees", async Task<Created<EmployeeDto>> (CreateEmployeeRequest body, EmployeeService s, CancellationToken ct) =>
        {
            var dto = await s.CreateAsync(body, ct);
            return TypedResults.Created($"/api/v1/people/employees/{dto.Id}", dto);
        }).WithName("CreateEmployee");
        p.MapPut("/employees/{id:guid}", async Task<Ok<EmployeeDto>> (Guid id, UpdateEmployeeRequest body, EmployeeService s, CancellationToken ct)
            => TypedResults.Ok(await s.UpdateAsync(id, body, ct))).WithName("UpdateEmployee");
        p.MapPut("/employees/{id:guid}/emergency-contacts", async Task<Ok<EmployeeDto>> (Guid id, ReplaceEmergencyContactsRequest body, EmployeeService s, CancellationToken ct)
            => TypedResults.Ok(await s.ReplaceContactsAsync(id, body, ct))).WithName("ReplaceEmergencyContacts");

        // Employment history
        p.MapGet("/employees/{id:guid}/history", async Task<Ok<EmployeeHistoryDto>> (Guid id, EmployeeService s, CancellationToken ct)
            => TypedResults.Ok(await s.HistoryAsync(id, ct))).WithName("GetEmployeeHistory");
        p.MapPost("/employees/{id:guid}/assignments", async Task<Ok<EmployeeHistoryDto>> (Guid id, AddAssignmentRequest body, EmployeeService s, CancellationToken ct)
            => TypedResults.Ok(await s.AddAssignmentAsync(id, body, ct))).WithName("AddAssignment");
        p.MapPost("/employees/{id:guid}/assignments/{versionId:guid}/correct", async Task<Ok<EmployeeHistoryDto>> (Guid id, Guid versionId, CorrectAssignmentRequest body, EmployeeService s, CancellationToken ct)
            => TypedResults.Ok(await s.CorrectAssignmentAsync(id, versionId, body, ct))).WithName("CorrectAssignment");
        p.MapPost("/employees/{id:guid}/assignments/{versionId:guid}/cancel", async Task<Ok<EmployeeHistoryDto>> (Guid id, Guid versionId, CancelAssignmentRequest body, EmployeeService s, CancellationToken ct)
            => TypedResults.Ok(await s.CancelAssignmentAsync(id, versionId, body, ct))).WithName("CancelAssignment");
        p.MapPost("/employees/{id:guid}/terminate", async Task<Ok<EmployeeHistoryDto>> (Guid id, TerminateRequest body, EmployeeService s, CancellationToken ct)
            => TypedResults.Ok(await s.TerminateAsync(id, body, ct))).WithName("TerminateEmployee");
        p.MapPost("/employees/{id:guid}/rescind-termination", async Task<Ok<EmployeeHistoryDto>> (Guid id, RescindTerminationRequest body, EmployeeService s, CancellationToken ct)
            => TypedResults.Ok(await s.RescindTerminationAsync(id, body, ct))).WithName("RescindTermination");
        p.MapPost("/employees/{id:guid}/rehire", async Task<Ok<EmployeeHistoryDto>> (Guid id, RehireRequest body, EmployeeService s, CancellationToken ct)
            => TypedResults.Ok(await s.RehireAsync(id, body, ct))).WithName("RehireEmployee");

        // Payment details (four-eyes)
        p.MapPost("/employees/{id:guid}/payment-changes", async Task<Ok<PaymentChangeDto>> (Guid id, RequestPaymentChangeRequest body, EmployeeService s, CancellationToken ct)
            => TypedResults.Ok(await s.RequestPaymentChangeAsync(id, body, ct))).WithName("RequestPaymentChange");
        p.MapGet("/payment-changes", async Task<Ok<PagedResult<PaymentChangeDto>>> (string? status, Guid? legalEntityId, int? page, int? pageSize, EmployeeService s, CancellationToken ct)
            => TypedResults.Ok(await s.ListPaymentChangesAsync(status, legalEntityId, page, pageSize, ct))).WithName("ListPaymentChanges");
        p.MapPost("/payment-changes/{id:guid}/approve", async Task<Ok<PaymentChangeDto>> (Guid id, DecidePaymentChangeRequest body, EmployeeService s, CancellationToken ct)
            => TypedResults.Ok(await s.ApprovePaymentChangeAsync(id, body, ct))).WithName("ApprovePaymentChange");
        p.MapPost("/payment-changes/{id:guid}/reject", async Task<Ok<PaymentChangeDto>> (Guid id, DecidePaymentChangeRequest body, EmployeeService s, CancellationToken ct)
            => TypedResults.Ok(await s.RejectPaymentChangeAsync(id, body, ct))).WithName("RejectPaymentChange");

        p.MapGet("/work-permit-alerts", async Task<Ok<IReadOnlyList<WorkPermitAlertDto>>> (Guid? legalEntityId, EmployeeService s, CancellationToken ct)
            => TypedResults.Ok(await s.WorkPermitAlertsAsync(legalEntityId, ct))).WithName("ListWorkPermitAlerts");

        // Bank branch reference table
        p.MapGet("/bank-branches", async Task<Ok<IReadOnlyList<BankBranchDto>>> (bool? includeInactive, BankBranchService s, CancellationToken ct)
            => TypedResults.Ok(await s.ListAsync(includeInactive, ct))).WithName("ListBankBranches");
        p.MapPost("/bank-branches", async Task<Created<BankBranchDto>> (CreateBankBranchRequest body, BankBranchService s, CancellationToken ct) =>
        {
            var dto = await s.CreateAsync(body, ct);
            return TypedResults.Created($"/api/v1/people/bank-branches/{dto.Id}", dto);
        }).WithName("CreateBankBranch");
        p.MapPut("/bank-branches/{id:guid}", async Task<Ok<BankBranchDto>> (Guid id, UpdateBankBranchRequest body, BankBranchService s, CancellationToken ct)
            => TypedResults.Ok(await s.UpdateAsync(id, body, ct))).WithName("UpdateBankBranch");

        // Import wizard (MIG-01, employees)
        p.MapGet("/imports/columns", Ok<IReadOnlyList<ImportColumnDto>> () => TypedResults.Ok(ImportService.Columns)).WithName("ListImportColumns");
        p.MapGet("/imports/template", () => TypedResults.Text(ImportService.TemplateCsv(), "text/csv"))
            .WithName("DownloadImportTemplate").Produces<string>(200, "text/csv");
        p.MapPost("/imports", async Task<Created<ImportBatchDto>> (UploadImportRequest body, ImportService s, CancellationToken ct) =>
        {
            var dto = await s.UploadAsync(body, ct);
            return TypedResults.Created($"/api/v1/people/imports/{dto.Id}", dto);
        }).WithName("UploadImport");
        p.MapGet("/imports", async Task<Ok<PagedResult<ImportBatchDto>>> (Guid? legalEntityId, int? page, int? pageSize, ImportService s, CancellationToken ct)
            => TypedResults.Ok(await s.ListAsync(legalEntityId, page, pageSize, ct))).WithName("ListImports");
        p.MapGet("/imports/{id:guid}", async Task<Ok<ImportBatchDto>> (Guid id, ImportService s, CancellationToken ct)
            => TypedResults.Ok(await s.GetAsync(id, ct))).WithName("GetImport");
        p.MapGet("/imports/{id:guid}/rows", async Task<Ok<PagedResult<ImportRowDto>>> (Guid id, bool? onlyErrors, int? page, int? pageSize, ImportService s, CancellationToken ct)
            => TypedResults.Ok(await s.RowsAsync(id, onlyErrors, page, pageSize, ct))).WithName("ListImportRows");
        p.MapPost("/imports/{id:guid}/commit", async Task<Ok<ImportBatchDto>> (Guid id, ImportService s, CancellationToken ct)
            => TypedResults.Ok(await s.CommitAsync(id, ct))).WithName("CommitImport");
        p.MapPost("/imports/{id:guid}/discard", async Task<Ok<ImportBatchDto>> (Guid id, ImportService s, CancellationToken ct)
            => TypedResults.Ok(await s.DiscardAsync(id, ct))).WithName("DiscardImport");

        return app;
    }
}
