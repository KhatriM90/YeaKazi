using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using YeaKazi.BuildingBlocks.Errors;
using YeaKazi.BuildingBlocks.Persistence;
using YeaKazi.BuildingBlocks.Tenancy;

namespace YeaKazi.BuildingBlocks.Http;

/// <summary>Maps failures to RFC 7807 problem details with a stable "code" and the correlation id.</summary>
public sealed class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext http, RequestContext context)
    {
        try
        {
            await next(http);
        }
        catch (Exception ex) when (!http.Response.HasStarted)
        {
            var (status, code, title, errors) = Map(ex);
            if (status >= 500)
            {
                logger.LogError(ex, "Unhandled error {CorrelationId}", context.CorrelationId);
            }
            else
            {
                logger.LogInformation("Request failed {Code} {CorrelationId}", code, context.CorrelationId);
            }

            var problem = new ProblemDetails
            {
                Status = status,
                Title = title,
                Type = $"https://docs.yeakazi.app/errors/{code}",
            };
            problem.Extensions["code"] = code;
            problem.Extensions["correlationId"] = context.CorrelationId;
            if (errors is not null)
            {
                problem.Extensions["errors"] = errors;
            }

            http.Response.Clear();
            http.Response.StatusCode = status;
            http.Response.ContentType = "application/problem+json";
            await http.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
        }
    }

    internal static (int Status, string Code, string Title, IDictionary<string, string[]>? Errors) Map(Exception ex)
    {
        switch (ex)
        {
            case ValidationException v:
                return (v.Status, v.Code, v.Message, v.Errors);
            case AppException a:
                return (a.Status, a.Code, a.Message, null);
            case DbUpdateConcurrencyException:
                return (409, "concurrency_conflict", "The record was changed by someone else. Reload and try again.", null);
            case TenantScopeException:
                return (404, "not_found", "The record was not found.", null);
            case BadHttpRequestException b:
                return (400, "bad_request", b.Message, null);
        }

        var pg = ex as PostgresException ?? ex.InnerException as PostgresException;
        if (pg is not null)
        {
            return pg.SqlState switch
            {
                PostgresErrorCodes.UniqueViolation => (409, "duplicate", "A record with the same unique value already exists.", null),
                PostgresErrorCodes.ForeignKeyViolation => (422, "invalid_reference", "A referenced record does not exist in this tenant or employer.", null),
                PostgresErrorCodes.InsufficientPrivilege => (404, "not_found", "The record was not found.", null),
                PostgresErrorCodes.CheckViolation => (422, "rule_violation", "The change breaks a data rule.", null),
                _ => (500, "internal_error", "An unexpected error occurred.", null),
            };
        }

        return (500, "internal_error", "An unexpected error occurred.", null);
    }
}
