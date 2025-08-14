using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Shared.Ids;

namespace FusionOps.Domain.Interfaces;

public interface IHumanResourceRepository
{
    Task<HumanResource?> GetByIdAsync(HumanResourceId id, CancellationToken cancellationToken = default);
    Task AddAsync(HumanResource resource, CancellationToken cancellationToken = default);
    Task UpdateAsync(HumanResource resource, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<HumanResource>> GetAllAsync(CancellationToken cancellationToken = default);
}
