using FusionOps.Presentation.Security;

namespace FusionOps.Presentation.Middleware;

public sealed class TenantResolutionMiddleware : IMiddleware
{
    private readonly TenantProvider _provider;
    private readonly ILogger<TenantResolutionMiddleware> _log;

    public TenantResolutionMiddleware(TenantProvider provider, ILogger<TenantResolutionMiddleware> log)
        => (_provider, _log) = (provider, log);

    public async Task InvokeAsync(HttpContext ctx, RequestDelegate next)
    {
        string? claim = ctx.User?.FindFirst("tenant_id")?.Value;
        if (claim is null && ctx.Request.Host.Host.Split('.').Length > 2)
        {
            claim = ctx.Request.Host.Host.Split('.')[0];
        }
        if (claim is null)
        {
            claim = ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(claim))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync("Tenant is required");
            return;
        }

        var normalized = claim.Trim().ToLowerInvariant();
        _provider.Set(normalized);

        await next(ctx);
    }
}



