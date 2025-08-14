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
        builder.Property(s => s.Id)
               .HasConversion(id => id.Value, v => new FusionOps.Domain.Shared.Ids.StockItemId(v))
               .ValueGeneratedNever();

        builder.Property(s => s.Sku)
               .HasMaxLength(100)
               .IsRequired();

        builder.HasIndex(s => s.Sku).IsUnique();

        builder.ComplexProperty(s => s.UnitCost, rate =>
        {
            rate.Property(m => m.Amount)
                .HasColumnName("UnitCostAmount")
                .IsRequired();
            rate.Property(m => m.Currency)
                .HasConversion(c => c.Value, v => new FusionOps.Domain.Enumerations.Currency(v, string.Empty))
                .HasColumnName("UnitCostCurrency")
                .IsRequired();
        });
    }
}