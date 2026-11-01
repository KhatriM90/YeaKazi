using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using YeaKazi.BuildingBlocks.Http;
using YeaKazi.Organization.Services;

namespace YeaKazi.Organization.Endpoints;

public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app, string tenantPolicy)
    {
        var org = app.MapGroup("/api/v1/org").WithTags("Organization").RequireAuthorization(tenantPolicy);

        org.MapGet("/legal-entities", async Task<Ok<PagedResult<LegalEntityDto>>> ([AsParameters] OrgListQuery q, OrganizationService s, CancellationToken ct)
            => TypedResults.Ok(await s.ListLegalEntitiesAsync(q, ct))).WithName("ListLegalEntities");
        org.MapGet("/legal-entities/{id:guid}", async Task<Ok<LegalEntityDto>> (Guid id, OrganizationService s, CancellationToken ct)
            => TypedResults.Ok(await s.GetLegalEntityAsync(id, ct))).WithName("GetLegalEntity");
        org.MapPost("/legal-entities", async Task<Created<LegalEntityDto>> (CreateLegalEntityRequest body, OrganizationService s, CancellationToken ct) =>
        {
            var dto = await s.CreateLegalEntityAsync(body, ct);
            return TypedResults.Created($"/api/v1/org/legal-entities/{dto.Id}", dto);
        }).WithName("CreateLegalEntity");
        org.MapPut("/legal-entities/{id:guid}", async Task<Ok<LegalEntityDto>> (Guid id, UpdateLegalEntityRequest body, OrganizationService s, CancellationToken ct)
            => TypedResults.Ok(await s.UpdateLegalEntityAsync(id, body, ct))).WithName("UpdateLegalEntity");

        org.MapGet("/branches", async Task<Ok<PagedResult<BranchDto>>> ([AsParameters] OrgListQuery q, OrganizationService s, CancellationToken ct)
            => TypedResults.Ok(await s.ListBranchesAsync(q, ct))).WithName("ListBranches");
        org.MapGet("/branches/{id:guid}", async Task<Ok<BranchDto>> (Guid id, OrganizationService s, CancellationToken ct)
            => TypedResults.Ok(await s.GetBranchAsync(id, ct))).WithName("GetBranch");
        org.MapPost("/branches", async Task<Created<BranchDto>> (CreateBranchRequest body, OrganizationService s, CancellationToken ct) =>
        {
            var dto = await s.CreateBranchAsync(body, ct);
            return TypedResults.Created($"/api/v1/org/branches/{dto.Id}", dto);
        }).WithName("CreateBranch");
        org.MapPut("/branches/{id:guid}", async Task<Ok<BranchDto>> (Guid id, UpdateBranchRequest body, OrganizationService s, CancellationToken ct)
            => TypedResults.Ok(await s.UpdateBranchAsync(id, body, ct))).WithName("UpdateBranch");

        org.MapGet("/cost-centers", async Task<Ok<PagedResult<CostCenterDto>>> ([AsParameters] OrgListQuery q, OrganizationService s, CancellationToken ct)
            => TypedResults.Ok(await s.ListCostCentersAsync(q, ct))).WithName("ListCostCenters");
        org.MapGet("/cost-centers/{id:guid}", async Task<Ok<CostCenterDto>> (Guid id, OrganizationService s, CancellationToken ct)
            => TypedResults.Ok(await s.GetCostCenterAsync(id, ct))).WithName("GetCostCenter");
        org.MapPost("/cost-centers", async Task<Created<CostCenterDto>> (CreateCostCenterRequest body, OrganizationService s, CancellationToken ct) =>
        {
            var dto = await s.CreateCostCenterAsync(body, ct);
            return TypedResults.Created($"/api/v1/org/cost-centers/{dto.Id}", dto);
        }).WithName("CreateCostCenter");
        org.MapPut("/cost-centers/{id:guid}", async Task<Ok<CostCenterDto>> (Guid id, UpdateCostCenterRequest body, OrganizationService s, CancellationToken ct)
            => TypedResults.Ok(await s.UpdateCostCenterAsync(id, body, ct))).WithName("UpdateCostCenter");

        org.MapGet("/grades", async Task<Ok<PagedResult<GradeDto>>> ([AsParameters] OrgListQuery q, OrganizationService s, CancellationToken ct)
            => TypedResults.Ok(await s.ListGradesAsync(q, ct))).WithName("ListGrades");
        org.MapGet("/grades/{id:guid}", async Task<Ok<GradeDto>> (Guid id, OrganizationService s, CancellationToken ct)
            => TypedResults.Ok(await s.GetGradeAsync(id, ct))).WithName("GetGrade");
        org.MapPost("/grades", async Task<Created<GradeDto>> (CreateGradeRequest body, OrganizationService s, CancellationToken ct) =>
        {
            var dto = await s.CreateGradeAsync(body, ct);
            return TypedResults.Created($"/api/v1/org/grades/{dto.Id}", dto);
        }).WithName("CreateGrade");
        org.MapPut("/grades/{id:guid}", async Task<Ok<GradeDto>> (Guid id, UpdateGradeRequest body, OrganizationService s, CancellationToken ct)
            => TypedResults.Ok(await s.UpdateGradeAsync(id, body, ct))).WithName("UpdateGrade");

        org.MapGet("/departments", async Task<Ok<PagedResult<DepartmentDto>>> ([AsParameters] OrgListQuery q, OrganizationService s, CancellationToken ct)
            => TypedResults.Ok(await s.ListDepartmentsAsync(q, ct))).WithName("ListDepartments");
        org.MapGet("/departments/{id:guid}", async Task<Ok<DepartmentDto>> (Guid id, OrganizationService s, CancellationToken ct)
            => TypedResults.Ok(await s.GetDepartmentAsync(id, ct))).WithName("GetDepartment");
        org.MapPost("/departments", async Task<Created<DepartmentDto>> (CreateDepartmentRequest body, OrganizationService s, CancellationToken ct) =>
        {
            var dto = await s.CreateDepartmentAsync(body, ct);
            return TypedResults.Created($"/api/v1/org/departments/{dto.Id}", dto);
        }).WithName("CreateDepartment");
        org.MapPut("/departments/{id:guid}", async Task<Ok<DepartmentDto>> (Guid id, UpdateDepartmentRequest body, OrganizationService s, CancellationToken ct)
            => TypedResults.Ok(await s.UpdateDepartmentAsync(id, body, ct))).WithName("UpdateDepartment");

        org.MapGet("/positions", async Task<Ok<PagedResult<PositionDto>>> ([AsParameters] OrgListQuery q, OrganizationService s, CancellationToken ct)
            => TypedResults.Ok(await s.ListPositionsAsync(q, ct))).WithName("ListPositions");
        org.MapGet("/positions/{id:guid}", async Task<Ok<PositionDto>> (Guid id, OrganizationService s, CancellationToken ct)
            => TypedResults.Ok(await s.GetPositionAsync(id, ct))).WithName("GetPosition");
        org.MapPost("/positions", async Task<Created<PositionDto>> (CreatePositionRequest body, OrganizationService s, CancellationToken ct) =>
        {
            var dto = await s.CreatePositionAsync(body, ct);
            return TypedResults.Created($"/api/v1/org/positions/{dto.Id}", dto);
        }).WithName("CreatePosition");
        org.MapPut("/positions/{id:guid}", async Task<Ok<PositionDto>> (Guid id, UpdatePositionRequest body, OrganizationService s, CancellationToken ct)
            => TypedResults.Ok(await s.UpdatePositionAsync(id, body, ct))).WithName("UpdatePosition");

        org.MapGet("/holiday-calendars", async Task<Ok<PagedResult<HolidayCalendarDto>>> ([AsParameters] OrgListQuery q, OrganizationService s, CancellationToken ct)
            => TypedResults.Ok(await s.ListHolidayCalendarsAsync(q, ct))).WithName("ListHolidayCalendars");
        org.MapGet("/holiday-calendars/{id:guid}", async Task<Ok<HolidayCalendarDto>> (Guid id, OrganizationService s, CancellationToken ct)
            => TypedResults.Ok(await s.GetHolidayCalendarAsync(id, ct))).WithName("GetHolidayCalendar");
        org.MapPost("/holiday-calendars", async Task<Created<HolidayCalendarDto>> (CreateHolidayCalendarRequest body, OrganizationService s, CancellationToken ct) =>
        {
            var dto = await s.CreateHolidayCalendarAsync(body, ct);
            return TypedResults.Created($"/api/v1/org/holiday-calendars/{dto.Id}", dto);
        }).WithName("CreateHolidayCalendar");
        org.MapPut("/holiday-calendars/{id:guid}", async Task<Ok<HolidayCalendarDto>> (Guid id, UpdateHolidayCalendarRequest body, OrganizationService s, CancellationToken ct)
            => TypedResults.Ok(await s.UpdateHolidayCalendarAsync(id, body, ct))).WithName("UpdateHolidayCalendar");
        org.MapGet("/holiday-calendars/{id:guid}/holidays", async Task<Ok<IReadOnlyList<HolidayDto>>> (Guid id, int? year, bool? includeInactive, OrganizationService s, CancellationToken ct)
            => TypedResults.Ok(await s.ListHolidaysAsync(id, year, includeInactive, ct))).WithName("ListHolidays");
        org.MapPost("/holiday-calendars/{id:guid}/holidays", async Task<Created<HolidayDto>> (Guid id, CreateHolidayRequest body, OrganizationService s, CancellationToken ct) =>
        {
            var dto = await s.AddHolidayAsync(id, body, ct);
            return TypedResults.Created($"/api/v1/org/holiday-calendars/{id}/holidays/{dto.Id}", dto);
        }).WithName("AddHoliday");
        org.MapPut("/holiday-calendars/{id:guid}/holidays/{holidayId:guid}", async Task<Ok<HolidayDto>> (Guid id, Guid holidayId, UpdateHolidayRequest body, OrganizationService s, CancellationToken ct)
            => TypedResults.Ok(await s.UpdateHolidayAsync(id, holidayId, body, ct))).WithName("UpdateHoliday");

        org.MapPost("/holiday-calendars/{id:guid}/load-statutory", async Task<Ok<LoadStatutoryHolidaysResult>> (Guid id, LoadStatutoryHolidaysRequest body, OrganizationService s, CancellationToken ct)
            => TypedResults.Ok(await s.LoadStatutoryHolidaysAsync(id, body, ct))).WithName("LoadStatutoryHolidays");
        org.MapPost("/holidays/announce", async Task<Ok<AnnounceHolidayResult>> (AnnounceHolidayRequest body, OrganizationService s, CancellationToken ct)
            => TypedResults.Ok(await s.AnnounceHolidayAsync(body, ct))).WithName("AnnounceHoliday");

        org.MapGet("/work-schedules", async Task<Ok<PagedResult<WorkScheduleDto>>> ([AsParameters] OrgListQuery q, OrganizationService s, CancellationToken ct)
            => TypedResults.Ok(await s.ListWorkSchedulesAsync(q, ct))).WithName("ListWorkSchedules");
        org.MapGet("/work-schedules/{id:guid}", async Task<Ok<WorkScheduleDto>> (Guid id, OrganizationService s, CancellationToken ct)
            => TypedResults.Ok(await s.GetWorkScheduleAsync(id, ct))).WithName("GetWorkSchedule");
        org.MapPost("/work-schedules", async Task<Created<WorkScheduleDto>> (CreateWorkScheduleRequest body, OrganizationService s, CancellationToken ct) =>
        {
            var dto = await s.CreateWorkScheduleAsync(body, ct);
            return TypedResults.Created($"/api/v1/org/work-schedules/{dto.Id}", dto);
        }).WithName("CreateWorkSchedule");
        org.MapPut("/work-schedules/{id:guid}", async Task<Ok<WorkScheduleDto>> (Guid id, UpdateWorkScheduleRequest body, OrganizationService s, CancellationToken ct)
            => TypedResults.Ok(await s.UpdateWorkScheduleAsync(id, body, ct))).WithName("UpdateWorkSchedule");

        return app;
    }
}
