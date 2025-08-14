using System.Threading.Tasks;
using System.Collections.Generic;
using FusionOps.Domain.Shared.Interfaces;

namespace FusionOps.Domain.Interfaces;

public interface IUnitOfWork
{
    Task CommitAsync();
    Task RollbackAsync();

    /// <summary>
    /// Collects all pending domain events from tracked entities and clears them on the entities.
    /// </summary>
    IReadOnlyCollection<IDomainEvent> GetDomainEventsAndClear();
}