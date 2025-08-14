using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Interfaces;
using FusionOps.Domain.Shared.Ids;
using FusionOps.Infrastructure.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace FusionOps.Infrastructure.Repositories;

public class HumanResourceRepository : IHumanResourceRepository
{
    private readonly WorkforceContext _ctx;

    public HumanResourceRepository(WorkforceContext ctx)
    {
        _ctx = ctx;
    }

    public async Task<HumanResource?> GetByIdAsync(HumanResourceId id, CancellationToken cancellationToken = default)
    {
        return await _ctx.HumanResources.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task AddAsync(HumanResource resource, CancellationToken cancellationToken = default)
    {
        await _ctx.HumanResources.AddAsync(resource, cancellationToken);
    }

    public Task UpdateAsync(HumanResource resource, CancellationToken cancellationToken = default)
    {
        _ctx.HumanResources.Update(resource);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyCollection<HumanResource>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _ctx.HumanResources.ToListAsync(cancellationToken);
    }
}
