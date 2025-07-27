using FusionOps.Domain.Entities;
using FusionOps.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using FusionOps.Domain.Attributes;

namespace FusionOps.Infrastructure.Persistence.Postgres;

public class FulfillmentContext : DbContext
{
    public FulfillmentContext(DbContextOptions<FulfillmentContext> options) : base(options) { }

    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FulfillmentContext).Assembly);

        // apply PartitionedTable attribute annotation
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entity.ClrType;
            var attr = (PartitionedTableAttribute?)Attribute.GetCustomAttribute(clrType, typeof(PartitionedTableAttribute));
            if (attr is not null)
            {
                entity.SetAnnotation("PartitionedTable", attr.Strategy);
            }
        }

        // Ensure parent partitioned table exists
        Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS stock_items (
            id UUID PRIMARY KEY,
            sku TEXT NOT NULL,
            quantity INT NOT NULL,
            reorder_point INT NOT NULL,
            unit_cost NUMERIC(18,2) NOT NULL,
            created_at TIMESTAMPTZ DEFAULT now()
        ) PARTITION BY RANGE (date_trunc('month', created_at));");

        base.OnModelCreating(modelBuilder);
    }
}