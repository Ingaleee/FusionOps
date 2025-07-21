using System;
using System.Collections.Generic;
using FusionOps.Domain.Shared.Interfaces;
using FusionOps.Domain.Shared.Ids;
using FusionOps.Domain.ValueObjects;
using FusionOps.Domain.Enumerations;

namespace FusionOps.Domain.Entities;

/// <summary>
/// Represents an employee or contractor that can be allocated to projects.
/// </summary>
public class HumanResource : IEntity<HumanResourceId>, IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = new();

    public HumanResource(HumanResourceId id, string fullName, Money hourRate)
    {
        Id = id;
        FullName = fullName;
        HourRate = hourRate;
    }

    public HumanResourceId Id { get; }
    public string FullName { get; private set; }
    public HashSet<Skill> Skills { get; } = new();
    public Money HourRate { get; private set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();

    public void AddSkill(Skill skill) => Skills.Add(skill);

    public bool HasSkill(string name, SkillLevel level) => Skills.Contains(new Skill(name, level));

    public bool IsAvailable(ValueObjects.TimeRange period, IReadOnlyCollection<Allocation> calendar)
    {
        foreach (var allocation in calendar)
        {
            if (allocation.ResourceId.Equals(Id) && allocation.Period.Overlaps(period))
                return false;
        }
        return true;
    }
} 