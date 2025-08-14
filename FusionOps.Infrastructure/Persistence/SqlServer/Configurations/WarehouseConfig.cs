using FusionOps.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FusionOps.Infrastructure.Persistence.SqlServer.Configurations;

public class WarehouseConfig : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("Warehouses");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id)
               .HasConversion(id => id.Value, v => new FusionOps.Domain.Shared.Ids.WarehouseId(v))
               .ValueGeneratedNever();

        builder.Ignore(w => w.DomainEvents);
        builder.Ignore("_items");
    }
}



