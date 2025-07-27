using FusionOps.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FusionOps.Infrastructure.Persistence.Postgres.Configurations;

public class StockItemConfig : IEntityTypeConfiguration<StockItem>
{
    public void Configure(EntityTypeBuilder<StockItem> builder)
    {
        builder.ToTable("StockItems");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Sku)
               .HasMaxLength(100)
               .IsRequired();

        builder.HasIndex(s => s.Sku).IsUnique();

        builder.Ignore(s => s.UnitCost);
    }
}