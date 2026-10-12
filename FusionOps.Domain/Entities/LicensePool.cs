using System;
using System.Collections.Generic;
using FusionOps.Domain.Events;
using FusionOps.Domain.Shared;
using FusionOps.Domain.Shared.Interfaces;

namespace FusionOps.Domain.Entities;

/// <summary>
/// Aggregate root representing a pool of product licenses.
/// </summary>
public class LicensePool : IEntity<Guid>, IHasDomainEvents
{
    private readonly HashSet<LicenseAllocationRef> _allocations = new();
    private readonly List<IDomainEvent> _domainEvents = new();

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

        var available = TotalSeats - AllocatedSeats;
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
        if (TotalSeats - AllocatedSeats < seats) throw new LicenseCapacityException();
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
}

public record LicenseAllocationRef(Guid ProjectId, int Seats, Guid? ReservationId = null);
public sealed class LicenseCapacityException() : DomainException("Not enough license seats are available");


