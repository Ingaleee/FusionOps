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

        // Map strongly-typed key
        builder.Property(a => a.Id)
               .HasConversion(id => id.Value, v => new FusionOps.Domain.Shared.Ids.AllocationId(v))
               .ValueGeneratedNever();

        builder.Ignore(a => a.DomainEvents);
        // Map owned value object TimeRange
        builder.OwnsOne(a => a.Period, period =>
        {
            period.Property(p => p.Start)
                  .HasColumnName("PeriodStart")
                  .IsRequired();
            period.Property(p => p.End)
                  .HasColumnName("PeriodEnd")
                  .IsRequired();
        });

        builder.Property(a => a.ResourceId).IsRequired();
        builder.Property(a => a.ProjectId).IsRequired();

        builder.HasIndex(a => a.ResourceId);
        // Additional indexes can be configured in a migration later.
    }
}