using FusionOps.Domain.Events.Attributes;
using FusionOps.Domain.Shared.Interfaces;
using FusionOps.Domain.Workflows;

namespace FusionOps.Domain.Events;

[EventType("v1.ReservationWorkflowChanged")]
public sealed record ReservationWorkflowChanged(Guid WorkflowId, Guid ProjectId, long Version,
    WorkflowState State, WorkflowOutcome Outcome, string Reason) : IDomainEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;
}
