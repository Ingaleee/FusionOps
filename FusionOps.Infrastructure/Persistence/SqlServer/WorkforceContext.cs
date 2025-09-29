using FusionOps.Domain.Entities;
using FusionOps.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using FusionOps.Infrastructure.Persistence.SqlServer.Configurations;
using FusionOps.Infrastructure.Persistence.Common;
using FusionOps.Application.Abstractions;

namespace FusionOps.Infrastructure.Persistence.SqlServer;

public class WorkforceContext : DbContext
{
    private readonly ITenantProvider _tenantProvider;
    public WorkforceContext(DbContextOptions<WorkforceContext> options, ITenantProvider tenantProvider) : base(options)
    {
        _tenantProvider = tenantProvider;
    }

    public DbSet<HumanResource> HumanResources => Set<HumanResource>();
    public DbSet<EquipmentResource> EquipmentResources => Set<EquipmentResource>();
    public DbSet<Allocation> Allocations => Set<Allocation>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Apply only SQL Server configurations relevant to this context
        modelBuilder.ApplyConfiguration(new HumanResourceConfig());
        modelBuilder.ApplyConfiguration(new EquipmentResourceConfig());
        modelBuilder.ApplyConfiguration(new AllocationConfig());

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