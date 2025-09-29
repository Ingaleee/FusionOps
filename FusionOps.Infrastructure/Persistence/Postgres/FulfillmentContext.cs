using FusionOps.Domain.Entities;
using FusionOps.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using FusionOps.Domain.Attributes;
using FusionOps.Infrastructure.Persistence.Postgres.Models;
using FusionOps.Infrastructure.Persistence.Common;
using FusionOps.Application.Abstractions;

namespace FusionOps.Infrastructure.Persistence.Postgres;

public class FulfillmentContext : DbContext
{
    private readonly ITenantProvider _tenantProvider;
    public FulfillmentContext(DbContextOptions<FulfillmentContext> options, ITenantProvider tenantProvider) : base(options)
    {
        _tenantProvider = tenantProvider;
    }

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

        modelBuilder.AddTenantShadowProperty();
        if (_tenantProvider.IsSet)
        {
            modelBuilder.AddGlobalTenantFilter(_tenantProvider);
        }

        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State == EntityState.Added))
        {
            entry.Property("TenantId").CurrentValue = _tenantProvider.IsSet ? _tenantProvider.TenantId : null;
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}