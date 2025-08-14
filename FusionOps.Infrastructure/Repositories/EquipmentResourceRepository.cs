using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Interfaces;
using FusionOps.Domain.Shared.Ids;
using FusionOps.Infrastructure.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace FusionOps.Infrastructure.Repositories;

public class EquipmentResourceRepository : IEquipmentResourceRepository
{
    private readonly WorkforceContext _ctx;

    public EquipmentResourceRepository(WorkforceContext ctx)
    {
        _ctx = ctx;
    }

    public async Task<EquipmentResource?> GetByIdAsync(EquipmentResourceId id, CancellationToken cancellationToken = default)
    {
        return await _ctx.EquipmentResources.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task AddAsync(EquipmentResource resource, CancellationToken cancellationToken = default)
    {
        await _ctx.EquipmentResources.AddAsync(resource, cancellationToken);
    }

    public Task UpdateAsync(EquipmentResource resource, CancellationToken cancellationToken = default)
    {
        _ctx.EquipmentResources.Update(resource);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyCollection<EquipmentResource>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _ctx.EquipmentResources.ToListAsync(cancellationToken);
    }
}
