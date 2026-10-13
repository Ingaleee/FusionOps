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
        return await _ctx.Allocations.SingleOrDefaultAsync(a => a.Id == id);
    }

    public async Task<IReadOnlyCollection<Allocation>> FindForResourceAsync(Guid resourceId)
    {
        return await _ctx.Allocations
            .Where(a => a.TenantId == _ctx.TenantId && a.ResourceId == resourceId)
            .ToListAsync();
    }

    public async Task<IReadOnlyCollection<Allocation>> FindForResourceWithLockAsync(Guid resourceId, CancellationToken cancellationToken, bool requireActive = true)
    {
        if (_ctx.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Reservation checks require an active transaction");

        await CatalogLocks.RequireAsync(_ctx, resourceId, requireActive, cancellationToken);

        // An empty/sparse table can otherwise use a clustered scan and share broad PK ranges.
        // Both lock acquisition and entity materialization must seek the same tenant/resource index.
        // HOLDLOCK protects first use; the composite ownership migration installs this index.
        // EF's FromSql complex-value projection needs conventional aliases as well as retained columns.
        return await _ctx.Allocations.FromSqlInterpolated($"SELECT *, PeriodStart AS Period_Start, PeriodEnd AS Period_End FROM Allocations WITH (UPDLOCK, HOLDLOCK, INDEX(IX_Allocations_TenantId_ResourceId), FORCESEEK) WHERE TenantId = {_ctx.TenantId} AND ResourceId = {resourceId}")
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
