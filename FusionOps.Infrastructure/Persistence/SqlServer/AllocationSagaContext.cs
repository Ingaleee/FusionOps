using FusionOps.Infrastructure.Saga;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FusionOps.Infrastructure.Persistence.SqlServer;

public class AllocationSagaContext : SagaDbContext
{
    public AllocationSagaContext(DbContextOptions<AllocationSagaContext> options) : base(options) { }

    protected override IEnumerable<ISagaClassMap> Configurations
    {
        get { yield return new AllocationStateMap(); }
    }
}

public class AllocationStateMap : SagaClassMap<AllocationState>
{
    protected override void Configure(EntityTypeBuilder<AllocationState> entity, ModelBuilder model)
    {
        entity.Property(x => x.CurrentState).HasMaxLength(64);
        entity.Property(x => x.ProjectId);
        entity.Property(x => x.ReservedAt);
        entity.Property(x => x.ShippedAt);
    }
}