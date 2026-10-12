using FusionOps.Domain.Entities;
using FusionOps.Domain.Interfaces;
using FusionOps.Domain.Shared.Ids;
using FusionOps.Domain.ValueObjects;
using FusionOps.Infrastructure.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;
using FusionOps.Infrastructure.Planning;

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
        if (allocation == null) throw new ArgumentNullException(nameof(allocation));
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

    public async Task<IReadOnlyCollection<Allocation>> FindForResourceWithLockAsync(Guid resourceId, CancellationToken cancellationToken, bool requireActive = true)
    {
        if (_ctx.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Reservation checks require an active transaction");

        await CatalogLocks.RequireAsync(_ctx, resourceId, requireActive, cancellationToken);

        // HOLDLOCK protects the index key range even when this resource has no bookings yet.
        var sql = @"
            SELECT Id AS Value FROM Allocations WITH (UPDLOCK, HOLDLOCK)
            WHERE ResourceId = {0}";
        // Acquire the transaction's calendar locks independently of entity materialization.
        // LINQ then projects complex values using the configured physical column names.
        await _ctx.Database.SqlQueryRaw<Guid>(sql, resourceId).ToListAsync(cancellationToken);
        return await _ctx.Allocations.Where(a => a.ResourceId == resourceId).ToListAsync(cancellationToken);
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
