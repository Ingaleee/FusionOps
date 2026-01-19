using FusionOps.Domain.Entities;
using FusionOps.Domain.Interfaces;
using FusionOps.Domain.Shared.Ids;
using FusionOps.Domain.ValueObjects;
using FusionOps.Infrastructure.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace FusionOps.Infrastructure.Repositories;

public class AllocationRepository : IAllocationRepository
{
    private readonly WorkforceContext _ctx;
    public AllocationRepository(WorkforceContext ctx)
    {
        _ctx = ctx;
    }

    public async Task AddAsync(Allocation allocation)
    {
        await _ctx.Allocations.AddAsync(allocation);
    }

    public async Task<Allocation?> GetAsync(AllocationId id)
    {
        return await _ctx.Allocations.FindAsync(id);
    }

    public async Task<IReadOnlyCollection<Allocation>> FindForResourceAsync(Guid resourceId)
    {
        return await _ctx.Allocations
            .Where(a => a.ResourceId == resourceId)
            .ToListAsync();
    }

    public async Task<IReadOnlyCollection<Allocation>> FindForResourceWithLockAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        // Use UPDLOCK, ROWLOCK to prevent race conditions when checking for overlaps
        var sql = @"
            SELECT * FROM Allocations WITH (UPDLOCK, ROWLOCK)
            WHERE ResourceId = {0}";
        return await _ctx.Allocations
            .FromSqlRaw(sql, resourceId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<Allocation>> FindFreeAsync(TimeRange period)
    {
        // Allocations that DO NOT overlap with requested period
        return await _ctx.Allocations
            .Where(a => !(period.Start < a.Period.End && a.Period.Start < period.End))
            .ToListAsync();
    }

    public async Task<IReadOnlyCollection<Allocation>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await _ctx.Allocations.ToListAsync(cancellationToken);
    }
}