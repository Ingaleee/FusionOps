using FusionOps.Domain.Entities;
using FusionOps.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using FusionOps.Domain.Attributes;
using FusionOps.Infrastructure.Persistence.Postgres.Models;
using FusionOps.Infrastructure.Workflows;

namespace FusionOps.Infrastructure.Persistence.Postgres;

public class FulfillmentContext : FusionOps.Infrastructure.Tenancy.TenantDbContext
{
    public FulfillmentContext(DbContextOptions<FulfillmentContext> options, FusionOps.Application.Abstractions.TenantScope? scope = null) : base(options, scope) { }

    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<AllocationHistoryRow> AllocationHistory => Set<AllocationHistoryRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FulfillmentContext).Assembly,
            type => type.Namespace == "FusionOps.Infrastructure.Persistence.Postgres.Configurations"
                 || type == typeof(OutboxConfig));
        modelBuilder.ApplyConfiguration(new ReservationWorkflowConfig());
        modelBuilder.ApplyConfiguration(new WorkflowEffectReceiptConfig());
        modelBuilder.ApplyConfiguration(new FusionOps.Infrastructure.Projects.ProjectConfig());
        modelBuilder.ApplyConfiguration(new FusionOps.Infrastructure.Projects.ProjectRevisionConfig());
        modelBuilder.Entity<ProjectionCheckpoint>(checkpoint =>
        {
            checkpoint.ToTable("projection_checkpoints");
            checkpoint.HasKey(c => c.Name);
            checkpoint.Property(c => c.Name).HasMaxLength(100);
            checkpoint.Property(c => c.CommitPosition).IsConcurrencyToken();
            checkpoint.Property(c => c.PreparePosition).IsConcurrencyToken();
        });

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

        ConfigureTenancy(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }
}
