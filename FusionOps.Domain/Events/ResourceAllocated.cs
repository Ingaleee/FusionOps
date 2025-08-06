using System;
using FusionOps.Domain.Shared.Interfaces;
using FusionOps.Domain.Shared.Ids;
using FusionOps.Domain.ValueObjects;
using MediatR;
using FusionOps.Domain.Events.Attributes;

namespace FusionOps.Domain.Events;

[EventType("v1.ResourceAllocated")]
public class ResourceAllocated : DomainEvent
{
    public AllocationId AllocationId { get; }
    public Guid ResourceId { get; }
    public Guid ProjectId { get; }
    public TimeRange Period { get; }

    public ResourceAllocated(AllocationId allocationId,
                              Guid resourceId,
                              Guid projectId,
                              TimeRange period) : base()
    {
        AllocationId = allocationId;
        ResourceId = resourceId;
        ProjectId = projectId;
        Period = period;
    }
}