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
    public IReadOnlyCollection<LicenseAllocationRef> Allocations => _allocations;

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();

    public void IncreaseCapacity(int seats)
    {
        if (seats <= 0) throw new DomainException("Seats must be positive");
        TotalSeats += seats;
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

        _allocations.Add(new LicenseAllocationRef(projectId, seats));
        AllocatedSeats += seats;
        AddDomainEvent(new LicenseSeatsAllocated(Product, projectId, seats));
    }

    public void Release(Guid projectId, int seats)
    {
        if (seats <= 0) throw new DomainException("Seats must be positive");

        var key = new LicenseAllocationRef(projectId, seats);
        if (!_allocations.Contains(key))
        {
            // allow partial release by scanning allocations
            var released = 0;
            foreach (var alloc in _allocations)
            {
                if (alloc.ProjectId == projectId)
                {
                    released += alloc.Seats;
                }
            }
            if (released < seats)
            {
                throw new DomainException("Cannot release more seats than allocated for project");
            }

            // rebuild allocations excluding released seats (simple approach)
            var remaining = released - seats;
            _allocations.RemoveWhere(a => a.ProjectId == projectId);
            if (remaining > 0)
            {
                _allocations.Add(new LicenseAllocationRef(projectId, remaining));
            }
        }
        else
        {
            _allocations.Remove(key);
        }

        AllocatedSeats -= seats;
        if (AllocatedSeats < 0) AllocatedSeats = 0;
    }
}

public readonly record struct LicenseAllocationRef(Guid ProjectId, int Seats);


