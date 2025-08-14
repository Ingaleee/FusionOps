using System;
using FusionOps.Domain.Shared.Interfaces;
using FusionOps.Domain.Shared.Ids;
using FusionOps.Domain.ValueObjects;
using FusionOps.Domain.Events.Attributes;

namespace FusionOps.Domain.Events;

[EventType("v1.ResourceAllocated")]
public readonly record struct ResourceAllocated(
    AllocationId AllocationId,
    Guid ResourceId,
    Guid ProjectId,
    TimeRange Period) : IDomainEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}