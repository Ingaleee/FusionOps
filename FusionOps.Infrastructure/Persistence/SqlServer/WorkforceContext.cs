using FusionOps.Domain.Entities;
using FusionOps.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;

namespace FusionOps.Infrastructure.Persistence.SqlServer;

public class WorkforceContext : DbContext
{
    public WorkforceContext(DbContextOptions<WorkforceContext> options) : base(options) { }

    public DbSet<HumanResource> HumanResources => Set<HumanResource>();
    public DbSet<EquipmentResource> EquipmentResources => Set<EquipmentResource>();
    public DbSet<Allocation> Allocations => Set<Allocation>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutboxConfig());

        // Domain entity configurations will be applied in separate configuration classes (added later)
    }
} 