using System.Threading.Tasks;
using System.Collections.Generic;
using FusionOps.Domain.Shared.Interfaces;

namespace FusionOps.Domain.Interfaces;

public interface IUnitOfWork
{
    Task BeginAsync(PersistenceBoundary boundary, CancellationToken cancellationToken = default);
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);

}

public enum PersistenceBoundary { Workforce, Fulfillment }
