using System;
using FusionOps.Domain.Shared.Interfaces;
using FusionOps.Domain.Shared.Ids;
using FusionOps.Domain.Events.Attributes;

namespace FusionOps.Domain.Events;

/// <summary>
/// Emitted when an existing resource allocation is cancelled.
/// </summary>
[EventType("v1.ResourceAllocationCancelled")]
public readonly record struct ResourceAllocationCancelled(AllocationId AllocationId,
                                                          Guid ResourceId,
                                                          Guid ProjectId) : IDomainEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}