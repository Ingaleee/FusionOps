using FusionOps.Domain.Events.Attributes;
using FusionOps.Domain.Execution;
using FusionOps.Domain.Shared.Interfaces;

namespace FusionOps.Domain.Events;

[EventType("v1.ExecutionActivationChanged")]
public sealed record ExecutionActivationChanged(Guid PlanId, Guid ProjectId, long Version, ActivationState State, bool CancellationRequested, string Actor, string Reason) : IDomainEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;
}
