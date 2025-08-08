using FusionOps.Domain.Entities;
using FusionOps.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using FusionOps.Domain.Attributes;
using FusionOps.Infrastructure.Persistence.Postgres.Models;

namespace FusionOps.Infrastructure.Persistence.Postgres;

public class FulfillmentContext : DbContext
{
    public FulfillmentContext(DbContextOptions<FulfillmentContext> options) : base(options) { }

    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<AllocationHistoryRow> AllocationHistory => Set<AllocationHistoryRow>();

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

        base.OnModelCreating(modelBuilder);
    }
}