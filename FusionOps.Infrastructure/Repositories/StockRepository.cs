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
} 