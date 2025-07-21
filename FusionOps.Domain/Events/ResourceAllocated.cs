using System;
using FusionOps.Domain.Shared.Interfaces;
using FusionOps.Domain.Shared.Ids;
using FusionOps.Domain.ValueObjects;

namespace FusionOps.Domain.Events;

/// <summary>
/// Domain event raised after a resource has been successfully allocated to a project.
/// </summary>
public readonly record struct ResourceAllocated(AllocationId AllocationId,
                                                Guid ResourceId,
                                                Guid ProjectId,
                                                TimeRange Period) : IDomainEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
} 