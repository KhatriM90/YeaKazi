using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using YeaKazi.BuildingBlocks.Tenancy;

namespace YeaKazi.BuildingBlocks.Http;

/// <summary>Accepts a safe X-Correlation-Id or creates one; echoes it on every response.</summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string Header = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext http, RequestContext context)
    {
        var incoming = http.Request.Headers[Header].ToString();
        context.CorrelationId = !string.IsNullOrEmpty(incoming) && SafeId().IsMatch(incoming)
            ? incoming
            : Guid.NewGuid().ToString("N");
        context.ClientIp = http.Connection.RemoteIpAddress?.ToString();
        http.TraceIdentifier = context.CorrelationId;
        http.Response.OnStarting(() =>
        {
            http.Response.Headers[Header] = context.CorrelationId;
            return Task.CompletedTask;
        });
        await next(http);
    }

    [GeneratedRegex("^[A-Za-z0-9-]{8,64}$")]
    private static partial Regex SafeId();
}
