using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Shared.Ids;

namespace FusionOps.Domain.Interfaces;

public interface IEquipmentResourceRepository
{
    Task<EquipmentResource?> GetByIdAsync(EquipmentResourceId id, CancellationToken cancellationToken = default);
    Task AddAsync(EquipmentResource resource, CancellationToken cancellationToken = default);
    Task UpdateAsync(EquipmentResource resource, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<EquipmentResource>> GetAllAsync(CancellationToken cancellationToken = default);
}
