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
        builder.Ignore(x => x.DomainEvents);
        builder.Property(x => x.Version).IsConcurrencyToken();

        builder.OwnsMany(x => x.Allocations, (Action<OwnedNavigationBuilder<LicensePool, LicenseAllocationRef>>)(b =>
        {
            b.ToTable("license_allocations");
            b.WithOwner().HasForeignKey("TenantId", "pool_id").HasPrincipalKey(nameof(LicensePool.TenantId), nameof(LicensePool.Id));
            b.Property<Guid>("id").ValueGeneratedOnAdd();
            b.HasKey("id");
            b.Property(p => p.ProjectId).HasColumnName("project_id");
            b.Property(p => p.Seats).HasColumnName("seats");
            b.Property(p => p.ReservationId).HasColumnName("reservation_id");
            b.HasIndex("pool_id", nameof(LicenseAllocationRef.ReservationId)).IsUnique();
        }));

        builder.HasIndex(x => new { x.TenantId, x.Product }).IsUnique();
    }
}


