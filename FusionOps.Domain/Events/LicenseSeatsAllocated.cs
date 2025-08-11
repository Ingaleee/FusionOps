using FusionOps.Domain.Events.Attributes;
using FusionOps.Domain.Shared.Interfaces;

namespace FusionOps.Domain.Events;

[EventType("license_seats_allocated")]
public sealed record LicenseSeatsAllocated(string Product, System.Guid ProjectId, int Seats) : IDomainEvent
{
    public System.Guid Id { get; } = System.Guid.NewGuid();
    public System.DateTimeOffset OccurredOn { get; } = System.DateTimeOffset.UtcNow;
}


