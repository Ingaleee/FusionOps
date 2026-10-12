using System.Security.Claims;
using FusionOps.Application.Abstractions;

namespace FusionOps.Presentation.Infrastructure;

public sealed class HttpExecutionContext(IHttpContextAccessor accessor) : IExecutionContext
{
    public string Actor => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? accessor.HttpContext?.User.Identity?.Name ?? "system";
    public string CorrelationId => accessor.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString("N");
}
