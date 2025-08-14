using System;
using System.Collections.Generic;
using FusionOps.Domain.Events;
using FusionOps.Domain.Shared.Interfaces;
using FusionOps.Domain.Shared.Ids;
using FusionOps.Domain.ValueObjects;

namespace FusionOps.Domain.Entities;

/// <summary>
/// Aggregate root that captures reservation of a resource for a time period.
/// </summary>
public class Allocation : IEntity<AllocationId>, IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = new();

    // For EF
    private Allocation() { }

    private Allocation(AllocationId id, Guid resourceId, Guid projectId, TimeRange period)
    {
        Id = id;
        ResourceId = resourceId;
        ProjectId = projectId;
        Period = period;
    }

    public AllocationId Id { get; private set; }
    public Guid ResourceId { get; private set; }
    public Guid ProjectId { get; private set; }
    public TimeRange Period { get; private set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();

    public static Allocation Reserve(Guid resourceId, Guid projectId, TimeRange period,
                                      IReadOnlyCollection<Allocation> existing)
    {
        foreach (var alloc in existing)
        {
            if (alloc.ResourceId == resourceId && alloc.Period.Overlaps(period))
                throw new InvalidOperationException("Resource already allocated for given period");
        }

        var allocation = new Allocation(AllocationId.New(), resourceId, projectId, period);
        allocation.AddDomainEvent(new ResourceAllocated(allocation.Id, resourceId, projectId, period));
        return allocation;
    }

    public Allocation Cancel()
    {
        AddDomainEvent(new ResourceAllocationCancelled(Id, ResourceId, ProjectId));
        return this;
    }
}