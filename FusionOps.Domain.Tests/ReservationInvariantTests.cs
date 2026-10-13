using FusionOps.Domain.Entities;
using FusionOps.Domain.Shared;
using FusionOps.Domain.ValueObjects;

namespace FusionOps.Domain.Tests;

public sealed class ReservationInvariantTests
{
    [Test]
    public void CancelledBookingNoLongerBlocksTheCalendarAndEmitsOneCancellation()
    {
        var resource = Guid.NewGuid();
        var period = new TimeRange(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
        var old = Allocation.Reserve(resource, Guid.NewGuid(), period, []);
        Assert.Throws<AllocationConflictException>(() => Allocation.Reserve(resource, Guid.NewGuid(), period, [old]));
        old.ClearDomainEvents();
        old.Cancel().Cancel();
        Assert.That(old.DomainEvents.Count, Is.EqualTo(1));
        Assert.DoesNotThrow(() => Allocation.Reserve(resource, Guid.NewGuid(), period, [old]));
    }

    [Test]
    public void RepeatedLicenseReservationsRemainReleasableAndCountersAgree()
    {
        var project = Guid.NewGuid();
        var pool = new LicensePool("compiler", 20);
        pool.Allocate(project, 3);
        pool.Allocate(project, 3);
        Assert.That(pool.Allocations.Single().Seats, Is.EqualTo(6));
        pool.Release(project, 6);
        Assert.That(pool.AllocatedSeats, Is.Zero);
        Assert.That(pool.Allocations, Is.Empty);
    }

    [Test]
    public void HalfOpenUtcIntervalsSplitWithoutGapsOrZeroLengthSegments()
    {
        var period = new TimeRange(DateTimeOffset.Parse("2026-10-12T23:00:00+03:00"), DateTimeOffset.Parse("2026-10-14T03:00:00+03:00"));
        var segments = period.SplitByDay();
        Assert.That(period.Start.Offset, Is.EqualTo(TimeSpan.Zero));
        Assert.That(period.Contains(period.End), Is.False);
        Assert.That(segments.Length, Is.EqualTo(2));
        Assert.That(segments[0].End, Is.EqualTo(segments[1].Start));
        Assert.That(segments.Sum(s => (s.End - s.Start).Ticks), Is.EqualTo((period.End - period.Start).Ticks));
        Assert.Throws<ArgumentException>(() => new TimeRange(period.Start, period.Start));
    }
}
