using System;
using System.Collections.Generic;
using FusionOps.Domain.Events;
using FusionOps.Domain.Shared;
using FusionOps.Domain.Shared.Interfaces;

namespace FusionOps.Domain.Entities;

/// <summary>
/// Aggregate root representing a pool of product licenses.
/// </summary>
public class LicensePool : FusionOps.Domain.Tenancy.TenantOwnedEntity, IEntity<Guid>, IHasDomainEvents
{
    private readonly HashSet<LicenseAllocationRef> _allocations = new();
    private readonly List<IDomainEvent> _domainEvents = new();
    private readonly List<TemporalLicenseReservation> _temporalReservations = [];

    // For EF
    private LicensePool() { }

    public LicensePool(string product, int totalSeats)
    {
        Id = Guid.NewGuid();
        Product = product ?? throw new ArgumentNullException(nameof(product));
        TotalSeats = totalSeats >= 0 ? totalSeats : throw new ArgumentOutOfRangeException(nameof(totalSeats));
        AllocatedSeats = 0;
    }

    public Guid Id { get; private set; }
    public string Product { get; private set; } = string.Empty;
    public int TotalSeats { get; private set; }
    public int AllocatedSeats { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyCollection<LicenseAllocationRef> Allocations => _allocations.ToArray();
    public IReadOnlyCollection<TemporalLicenseReservation> TemporalReservations => _temporalReservations.AsReadOnly();
    public long TemporalReservedPeak => Peak(_temporalReservations.Select(r => (r.Period, r.Seats)));

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();

    public void IncreaseCapacity(int seats)
    {
        if (seats <= 0) throw new DomainException("Seats must be positive");
        TotalSeats += seats;
        Version++;
    }

    public void Allocate(Guid projectId, int seats)
    {
        if (seats <= 0) throw new DomainException("Seats must be positive");

        var available = TotalSeats - AllocatedSeats - checked((int)TemporalReservedPeak);
        if (seats > available)
        {
            AddDomainEvent(new LicenseViolationDetected(Product, seats - available));
            throw new DomainException("Not enough licenses available");
        }

        var currentSeats = _allocations.Where(a => a.ProjectId == projectId && a.ReservationId is null).Sum(a => a.Seats);
        _allocations.RemoveWhere(a => a.ProjectId == projectId && a.ReservationId is null);
        _allocations.Add(new LicenseAllocationRef(projectId, currentSeats + seats));
        AllocatedSeats += seats;
        Version++;
        AddDomainEvent(new LicenseSeatsAllocated(Product, projectId, seats));
    }

    public void Release(Guid projectId, int seats)
    {
        if (seats <= 0) throw new DomainException("Seats must be positive");
        var ordinary = _allocations.Where(a => a.ProjectId == projectId && a.ReservationId is null).ToArray();
        var currentSeats = ordinary.Sum(a => a.Seats);
        if (currentSeats < seats) throw new DomainException("Cannot release more ordinary seats than allocated for project");
        foreach (var allocation in ordinary) _allocations.Remove(allocation);
        if (currentSeats > seats) _allocations.Add(new(projectId, currentSeats - seats));
        AllocatedSeats -= seats;
        Version++;
        AddDomainEvent(new LicenseSeatsReleased(Product, projectId, seats, null));
    }

    public void Reserve(Guid reservationId, Guid projectId, int seats)
    {
        if (reservationId == Guid.Empty || projectId == Guid.Empty || seats <= 0) throw new DomainException("Reservation identity and positive seats are required");
        var existing = _allocations.SingleOrDefault(a => a.ReservationId == reservationId);
        if (existing is not null)
        {
            if (existing.ProjectId != projectId || existing.Seats != seats) throw new DomainException("The license reservation identity was reused with different data");
            return;
        }
        if (TotalSeats - AllocatedSeats - TemporalReservedPeak < seats) throw new LicenseCapacityException();
        _allocations.Add(new(projectId, seats, reservationId));
        AllocatedSeats += seats;
        Version++;
        AddDomainEvent(new LicenseSeatsAllocated(Product, projectId, seats));
    }

    public void ReleaseReservation(Guid reservationId)
    {
        var allocation = _allocations.SingleOrDefault(a => a.ReservationId == reservationId);
        if (allocation is null) return;
        _allocations.Remove(allocation);
        AllocatedSeats -= allocation.Seats;
        Version++;
        AddDomainEvent(new LicenseSeatsReleased(Product, allocation.ProjectId, allocation.Seats, reservationId));
    }

    public void ReserveTemporal(Guid planId, Guid projectId, IReadOnlyList<TemporalLicenseDemand> demands)
    {
        if (planId == Guid.Empty || projectId == Guid.Empty || demands is not { Count: > 0 and <= 200 }
            || demands.Any(d => d is null || d.TaskId == Guid.Empty || d.Seats is < 1 or > 1_000_000 || d.Period is null)
            || demands.Select(d => d.TaskId).Distinct().Count() != demands.Count)
            throw new DomainException("Invalid temporal license specification");
        var existing = _temporalReservations.Where(r => r.PlanId == planId).ToArray();
        if (existing.Length > 0)
        {
            if (existing.Length != demands.Count || existing.Any(r => r.ProjectId != projectId
                || !demands.Any(d => d.TaskId == r.TaskId && d.Seats == r.Seats && d.Period == r.Period)))
                throw new DomainException("Temporal license identity was reused with different data");
            return;
        }
        var peak = Peak(_temporalReservations.Select(r => (r.Period, r.Seats)).Concat(demands.Select(d => (d.Period, d.Seats))));
        if (peak + AllocatedSeats > TotalSeats) throw new LicenseCapacityException();
        _temporalReservations.AddRange(demands.OrderBy(d => d.TaskId).Select(d => new TemporalLicenseReservation(planId, projectId, d)));
        Version++;
        AddDomainEvent(new TemporalLicenseCapacityChanged(Id, Product, projectId, planId, Version, demands.Count, false));
    }
    public void ReleaseTemporal(Guid planId, Guid projectId)
    {
        var owned = _temporalReservations.Where(r => r.PlanId == planId).ToArray();
        if (owned.Any(r => r.ProjectId != projectId)) throw new DomainException("Temporal license ownership changed");
        if (owned.Length == 0) return;
        _temporalReservations.RemoveAll(r => r.PlanId == planId); Version++;
        AddDomainEvent(new TemporalLicenseCapacityChanged(Id, Product, projectId, planId, Version, owned.Length, true));
    }
    private static long Peak(IEnumerable<(FusionOps.Domain.ValueObjects.TimeRange Period, int Seats)> reservations)
    {
        long used = 0, peak = 0;
        foreach (var change in reservations.SelectMany(r => new[] { (At: r.Period.Start, Delta: r.Seats), (At: r.Period.End, Delta: -r.Seats) })
            .OrderBy(c => c.At).ThenBy(c => c.Delta))
        { used += change.Delta; peak = Math.Max(peak, used); }
        return peak;
    }
}

public record LicenseAllocationRef(Guid ProjectId, int Seats, Guid? ReservationId = null);
public sealed class LicenseCapacityException() : DomainException("Not enough license seats are available");

public sealed record TemporalLicenseDemand(Guid TaskId, int Seats, FusionOps.Domain.ValueObjects.TimeRange Period);
public sealed class TemporalLicenseReservation
{
    private TemporalLicenseReservation() { }
    internal TemporalLicenseReservation(Guid planId, Guid projectId, TemporalLicenseDemand demand)
    { PlanId = planId; ProjectId = projectId; TaskId = demand.TaskId; Seats = demand.Seats; Period = new(demand.Period.Start, demand.Period.End); }
    public Guid PlanId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid TaskId { get; private set; }
    public int Seats { get; private set; }
    public FusionOps.Domain.ValueObjects.TimeRange Period { get; private set; } = null!;
}


