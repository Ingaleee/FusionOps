using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace FusionOps.Presentation.Authorization;

public class AuditAuthorizationHandler : AuthorizationHandler<AuditReadRequirement>
{
    private readonly ILogger<AuditAuthorizationHandler> _logger;

    public AuditAuthorizationHandler(ILogger<AuditAuthorizationHandler> logger)
    {
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, 
        AuditReadRequirement requirement)
    {
        var user = context.User;
        
        // Проверяем роли для аудита
        if (user.IsInRole("AuditViewer") || user.IsInRole("Admin"))
        {
            _logger.LogInformation("User {UserId} authorized for audit read", user.Identity?.Name);
            context.Succeed(requirement);
        }
        else
        {
            _logger.LogWarning("User {UserId} denied audit read access", user.Identity?.Name);
        }

        return Task.CompletedTask;
    }
}

public class AuditReadRequirement : IAuthorizationRequirement
{
    public const string PolicyName = "Audit.Read";
}
