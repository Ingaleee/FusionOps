using System.Security.Claims;
using FusionOps.Application.Abstractions;
using FusionOps.Domain.Tenancy;

namespace FusionOps.Presentation.Middleware;

public sealed class TenantMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    public const string Claim = "tenant_id";
    public const string DevelopmentHeader = "X-Tenant-Id";
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api") && !context.Request.Path.StartsWithSegments("/hubs")) { await next(context); return; }
        Guid tenant;
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var claims = context.User.FindAll(Claim).ToArray();
            var subjects = context.User.FindAll(ClaimTypes.NameIdentifier).ToArray();
            if (claims.Length != 1 || !Guid.TryParseExact(claims[0].Value, "D", out tenant) || tenant == Guid.Empty
                || subjects.Length != 1 || string.IsNullOrWhiteSpace(subjects[0].Value) || subjects[0].Value.Length > 200)
            { await Reject(context, "tenant_claim_required", "A single tenant_id and subject are required in the verified token"); return; }
            if (context.Request.Headers.ContainsKey(DevelopmentHeader))
            { await Reject(context, "tenant_override_forbidden", "A request header cannot override the verified tenant"); return; }
        }
        else if (environment.IsDevelopment())
        {
            tenant = TenantIdentity.Legacy;
            if (context.Request.Headers.TryGetValue(DevelopmentHeader, out var values)
                && (values.Count != 1 || !Guid.TryParseExact(values.ToString(), "D", out tenant) || tenant == Guid.Empty))
            { await Reject(context, "invalid_development_tenant", "Supply one nonempty tenant GUID"); return; }
        }
        else { await Reject(context, "tenant_claim_required", "A verified tenant context is required"); return; }
        using var scope = TenantExecution.Enter(TenantScope.For(tenant));
        await next(context);
    }
    private static Task Reject(HttpContext context, string type, string title) => Results.Problem(statusCode: 403, type: type, title: title).ExecuteAsync(context);
}
