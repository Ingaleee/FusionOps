using System;
using FusionOps.Domain.Shared.Interfaces;
using FusionOps.Domain.Shared.Ids;
using FusionOps.Domain.ValueObjects;
using FusionOps.Domain.Events.Attributes;

namespace FusionOps.Domain.Events;

[EventType("v1.ResourceAllocated")]
public sealed record ResourceAllocated(
    AllocationId AllocationId,
    Guid ResourceId,
    Guid ProjectId,
    TimeRange Period) : IDomainEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;
}