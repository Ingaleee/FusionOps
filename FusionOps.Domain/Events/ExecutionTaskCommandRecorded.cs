using FusionOps.Domain.Events.Attributes;
using FusionOps.Domain.Execution;
using FusionOps.Domain.Shared.Interfaces;

namespace FusionOps.Domain.Events;
[EventType("v1.ExecutionTaskCommandRecorded")]
public sealed record ExecutionTaskCommandRecorded(Guid PlanId, Guid ProjectId, Guid TaskId, Guid CommandId, ExecutionTaskMutation Mutation, bool Accepted, string? RejectionCode, long TaskVersion, string Actor) : IDomainEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;
}
