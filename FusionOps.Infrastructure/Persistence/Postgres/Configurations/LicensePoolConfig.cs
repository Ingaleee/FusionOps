using FusionOps.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FusionOps.Infrastructure.Persistence.Postgres.Configurations;

public class LicensePoolConfig : IEntityTypeConfiguration<LicensePool>
{
    public void Configure(EntityTypeBuilder<LicensePool> builder)
    {
        builder.ToTable("license_pools");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Product).IsRequired().HasMaxLength(128);
        builder.Property(x => x.TotalSeats).IsRequired();
        builder.Property(x => x.AllocatedSeats).IsRequired();

        builder.OwnsMany(x => x.Allocations, b =>
        {
            b.ToTable("license_allocations");
            b.WithOwner().HasForeignKey("pool_id");
            b.Property<Guid>("id").ValueGeneratedOnAdd();
            b.HasKey("id");
            b.Property(p => p.ProjectId).HasColumnName("project_id");
            b.Property(p => p.Seats).HasColumnName("seats");
        });

        builder.HasIndex(x => x.Product).IsUnique();
    }
}


