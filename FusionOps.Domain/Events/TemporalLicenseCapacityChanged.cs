using FusionOps.Domain.Events.Attributes;
using FusionOps.Domain.Shared.Interfaces;

namespace FusionOps.Domain.Events;

[EventType("v1.TemporalLicenseCapacityChanged")]
public sealed record TemporalLicenseCapacityChanged(Guid PoolId, string Product, Guid ProjectId, Guid PlanId, long Version, int BookingCount, bool Released) : IDomainEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;
}
