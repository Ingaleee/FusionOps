using FusionOps.Domain.Events.Attributes;
using FusionOps.Domain.Shared.Interfaces;

namespace FusionOps.Domain.Events;

[EventType("license_violation_detected")]
public sealed record LicenseViolationDetected(string Product, int ShortageSeats) : IDomainEvent
{
    public System.Guid Id { get; } = System.Guid.NewGuid();
    public System.DateTimeOffset OccurredOn { get; } = System.DateTimeOffset.UtcNow;
}


