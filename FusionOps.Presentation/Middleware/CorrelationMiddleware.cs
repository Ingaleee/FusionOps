using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace FusionOps.Presentation.Middleware;

public class CorrelationMiddleware
{
    private const string HeaderName = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    public CorrelationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var cid) && !string.IsNullOrWhiteSpace(cid)
            ? cid.ToString()
            : Guid.NewGuid().ToString();

        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("UserId", context.User.Identity?.Name ?? "anonymous"))
        {
            var activity = Activity.Current ?? new Activity("fusionops_request");
            if (activity.Id == null) activity.Start();
            activity.SetTag("correlation_id", correlationId);
            await _next(context);
        }
    }
}