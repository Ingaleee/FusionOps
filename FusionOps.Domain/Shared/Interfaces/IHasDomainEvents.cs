using System.Collections.Generic;

namespace FusionOps.Domain.Shared.Interfaces;

/// <summary>
/// Provides access to domain events emitted by an aggregate or entity.
/// </summary>
public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    void AddDomainEvent(IDomainEvent domainEvent);

    void ClearDomainEvents();
}