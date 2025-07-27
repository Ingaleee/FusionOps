using FusionOps.Domain.Entities;
using FusionOps.Domain.Interfaces;
using FusionOps.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

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
        // Используем COPY bulk insert/update через NpgsqlCopy
        await using var conn = (Npgsql.NpgsqlConnection)_ctx.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        // create temp table
        await using (var create = new Npgsql.NpgsqlCommand("CREATE TEMP TABLE IF NOT EXISTS temp_stock_delta (sku text, delta integer);", conn))
        {
            await create.ExecuteNonQueryAsync();
        }

        await using var writer = conn.BeginBinaryImport("COPY temp_stock_delta (sku, delta) FROM STDIN (FORMAT BINARY)");

        foreach (var d in deltas)
        {
            writer.StartRow();
            writer.Write(d.Sku, NpgsqlTypes.NpgsqlDbType.Varchar);
            writer.Write(d.QuantityDelta, NpgsqlTypes.NpgsqlDbType.Integer);
        }
        await writer.CompleteAsync();

        var sql = @"
            UPDATE stock_items s
            SET quantity = s.quantity + d.delta
            FROM temp_stock_delta d
            WHERE s.sku = d.sku;
            DROP TABLE temp_stock_delta;";

        await using var cmd = new Npgsql.NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}