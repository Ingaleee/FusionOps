using FusionOps.Domain.Entities;
using FusionOps.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using FusionOps.Infrastructure.Persistence.Postgres;

namespace FusionOps.Infrastructure.Repositories;

public sealed class LicenseRepository : ILicenseRepository
{
    private readonly FulfillmentContext _ctx;
    public LicenseRepository(FulfillmentContext ctx) => _ctx = ctx;

    public Task<LicensePool?> FindByProductAsync(string product, CancellationToken ct = default)
        => _ctx.Set<LicensePool>().Include(p => p.Allocations).FirstOrDefaultAsync(p => p.Product == product, ct);

    public async Task AddAsync(LicensePool pool, CancellationToken ct = default)
    {
        await _ctx.Set<LicensePool>().AddAsync(pool, ct);
        await _ctx.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(LicensePool pool, CancellationToken ct = default)
    {
        _ctx.Update(pool);
        await _ctx.SaveChangesAsync(ct);
    }
}


