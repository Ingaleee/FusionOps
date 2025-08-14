using System.Collections.Generic;
using FusionOps.Domain.Enumerations;
using FusionOps.Domain.Shared.Interfaces;
using FusionOps.Domain.Shared.Ids;
using FusionOps.Domain.ValueObjects;

namespace FusionOps.Domain.Entities;

/// <summary>
/// Physical equipment that can be reserved for project execution.
/// </summary>
public class EquipmentResource : IEntity<EquipmentResourceId>, IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = new();

    private EquipmentResource() { }

    public EquipmentResource(EquipmentResourceId id, string model, EquipmentType type, Money hourRate)
    {
        Id = id;
        Model = model;
        Type = type;
        HourRate = hourRate;
    }

    public EquipmentResourceId Id { get; private set; }
    public string Model { get; private set; }
    public EquipmentType Type { get; private set; }
    public Money HourRate { get; private set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();

    public void ChangeHourlyRate(Money newRate)
    {
        if (newRate.Currency != HourRate.Currency)
            throw new System.InvalidOperationException("Currency must match existing rate");
        HourRate = newRate;
    }
}