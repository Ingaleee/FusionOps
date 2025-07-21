using FusionOps.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FusionOps.Infrastructure.Persistence.SqlServer.Configurations;

public class AllocationConfig : IEntityTypeConfiguration<Allocation>
{
    public void Configure(EntityTypeBuilder<Allocation> builder)
    {
        builder.ToTable("Allocations");
        builder.HasKey(a => a.Id);

        builder.Ignore(a => a.DomainEvents);
        builder.Ignore(a => a.Period);

        builder.Property(a => a.ResourceId).IsRequired();
        builder.Property(a => a.ProjectId).IsRequired();

        builder.HasIndex(a => a.ResourceId);
        // Additional indexes can be configured in a migration later.
    }
} 