using System.Collections.Generic;
using System.Threading.Tasks;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Shared.Ids;

namespace FusionOps.Domain.Interfaces;

/// <summary>
/// Repository abstraction for accessing stock items within warehouses.
/// </summary>
public interface IStockRepository
{
    Task<StockItem?> GetBySkuAsync(string sku);
    Task<IReadOnlyCollection<StockItem>> GetLowStockAsync();
    Task UpdateAsync(StockItem item);
    /// <summary>
    /// Массовое пополнение склада: быстрое обновление количества для списка позиций.
    /// </summary>
    Task BulkRestockAsync(IEnumerable<StockItemDelta> deltas);
    Task<IReadOnlyCollection<StockItem>> GetAllAsync(CancellationToken cancellationToken);
}