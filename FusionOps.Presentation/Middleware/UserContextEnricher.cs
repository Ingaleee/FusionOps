using System.Security.Claims;
using Serilog.Context;

namespace FusionOps.Presentation.Middleware;

public class UserContextEnricher
{
    private readonly RequestDelegate _next;

    public UserContextEnricher(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";

        using (LogContext.PushProperty("user", userId))
        {
            await _next(context);
        }
    }
} 