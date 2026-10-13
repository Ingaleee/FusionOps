using FusionOps.Domain.Events.Attributes;
using FusionOps.Domain.Shared.Interfaces;

namespace FusionOps.Domain.Events;

[EventType("v1.TemporalScenarioCalculated")]
public sealed record TemporalScenarioCalculated(Guid ScenarioId, Guid ProjectId, long DefinitionVersion, string Digest, bool IsComplete) : IDomainEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;
}
