using FusionOps.Domain.Events.Attributes;
using FusionOps.Domain.Execution;
using FusionOps.Domain.Shared.Interfaces;

namespace FusionOps.Domain.Events;

[EventType("v1.ExecutionPlanChanged")]
public sealed record ExecutionPlanChanged(Guid PlanId, Guid ProjectId, long Version, string Digest, ExecutionPlanState State,
    string Actor, string Action) : IDomainEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;
}
