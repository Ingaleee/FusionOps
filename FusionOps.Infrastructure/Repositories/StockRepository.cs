using FusionOps.Domain.Entities;
using FusionOps.Domain.Interfaces;
using FusionOps.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace FusionOps.Infrastructure.Repositories;

public class StockRepository : IStockRepository
{
    private readonly FulfillmentContext _ctx;
    public StockRepository(FulfillmentContext ctx)
    {
        _ctx = ctx;
    }

    public async Task<StockItem?> GetBySkuAsync(string sku)
    {
        return await _ctx.StockItems.FirstOrDefaultAsync(s => s.Sku == sku);
    }

    public async Task<IReadOnlyCollection<StockItem>> GetLowStockAsync()
    {
        return await _ctx.StockItems
            .Where(s => s.Quantity <= s.ReorderPoint)
            .ToListAsync();
    }

    public async Task UpdateAsync(StockItem item)
    {
        _ctx.StockItems.Update(item);
        await Task.CompletedTask;
    }

    public async Task BulkRestockAsync(IEnumerable<StockItemDelta> deltas)
    {
        if (_ctx.Database.CurrentTransaction is null) throw new InvalidOperationException("Restocking requires the command transaction");
        var batch = deltas.ToArray();
        if (batch.Length > 1000 || batch.Any(d => string.IsNullOrWhiteSpace(d.Sku) || d.QuantityDelta <= 0))
            throw new ArgumentException("Restocking supports up to 1000 positive SKU deltas");
        foreach (var delta in batch.OrderBy(d => d.Sku, StringComparer.Ordinal))
        {
            var item = await GetBySkuAsync(delta.Sku) ?? throw new FusionOps.Domain.Shared.DomainException("SKU is absent in this tenant");
            item.Restock(delta.QuantityDelta);
        }
    }

    public async Task<IReadOnlyCollection<StockItem>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await _ctx.StockItems.ToListAsync(cancellationToken);
    }
}
