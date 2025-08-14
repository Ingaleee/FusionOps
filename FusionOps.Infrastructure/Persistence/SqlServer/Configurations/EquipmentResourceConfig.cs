using FusionOps.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FusionOps.Infrastructure.Persistence.SqlServer.Configurations;

public class EquipmentResourceConfig : IEntityTypeConfiguration<EquipmentResource>
{
    public void Configure(EntityTypeBuilder<EquipmentResource> builder)
    {
        builder.ToTable("EquipmentResources");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
               .HasConversion(id => id.Value, v => new FusionOps.Domain.Shared.Ids.EquipmentResourceId(v))
               .ValueGeneratedNever();

        builder.Property(e => e.Model)
               .HasMaxLength(200)
               .IsRequired();

        builder.Property(e => e.Type)
               .HasConversion(v => v.Value, v => new FusionOps.Domain.Enumerations.EquipmentType(v, string.Empty))
               .IsRequired();

        builder.Ignore(e => e.DomainEvents);

        // Map Money as complex value object
        builder.ComplexProperty(e => e.HourRate, rate =>
        {
            rate.Property(m => m.Amount)
                .HasColumnName("HourRateAmount")
                .IsRequired();
            rate.Property(m => m.Currency)
                .HasConversion(c => c.Value, v => new FusionOps.Domain.Enumerations.Currency(v, string.Empty))
                .HasColumnName("HourRateCurrency")
                .IsRequired();
        });
    }
}