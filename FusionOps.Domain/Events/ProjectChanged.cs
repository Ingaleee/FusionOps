using FusionOps.Domain.Events.Attributes;
using FusionOps.Domain.Shared.Interfaces;

namespace FusionOps.Domain.Events;

[EventType("v1.ProjectChanged")]
public sealed record ProjectChanged(Guid ProjectId, long Version, long DefinitionVersion, string DefinitionDigest,
    string Actor, string Change) : IDomainEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;
}
