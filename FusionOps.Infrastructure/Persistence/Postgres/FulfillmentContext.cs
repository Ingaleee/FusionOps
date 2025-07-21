using FusionOps.Domain.Entities;
using FusionOps.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;

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

        base.OnModelCreating(modelBuilder);
    }
} 