using FusionOps.Application.Abstractions;

namespace FusionOps.Infrastructure.Workflows;

internal sealed record WorkflowExecutionContext(Guid Id) : IExecutionContext
{
    public string Actor => $"workflow:{Id:N}";
    public string CorrelationId => $"workflow.{Id:N}";
}
