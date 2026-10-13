using FusionOps.Domain.Entities;
using FusionOps.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using FusionOps.Infrastructure.Persistence.SqlServer.Configurations;
using FusionOps.Infrastructure.Idempotency;
using FusionOps.Domain.Catalog;
using FusionOps.Domain.Services.Planning;
using FusionOps.Infrastructure.Planning;

namespace FusionOps.Infrastructure.Persistence.SqlServer;

public class WorkforceContext : FusionOps.Infrastructure.Tenancy.TenantDbContext
{
    public WorkforceContext(DbContextOptions<WorkforceContext> options, FusionOps.Application.Abstractions.TenantScope? scope = null) : base(options, scope) { }

    public DbSet<HumanResource> HumanResources => Set<HumanResource>();
    public DbSet<EquipmentResource> EquipmentResources => Set<EquipmentResource>();
    public DbSet<Allocation> Allocations => Set<Allocation>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<CommandReceipt> CommandReceipts => Set<CommandReceipt>();
    public DbSet<CatalogResource> ResourceCatalog => Set<CatalogResource>();
    public DbSet<PlanningQuote> PlanningQuotes => Set<PlanningQuote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Apply only SQL Server configurations relevant to this context
        modelBuilder.ApplyConfiguration(new HumanResourceConfig());
        modelBuilder.ApplyConfiguration(new EquipmentResourceConfig());
        modelBuilder.ApplyConfiguration(new AllocationConfig());
        modelBuilder.ApplyConfiguration(new OutboxConfig());
        modelBuilder.ApplyConfiguration(new CommandReceiptConfig());
        modelBuilder.ApplyConfiguration(new CatalogResourceConfig());
        modelBuilder.ApplyConfiguration(new PlanningQuoteConfig());

        ConfigureTenancy(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }
}
