using FusionOps.Domain.Entities;
using FusionOps.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FusionOps.Infrastructure.Persistence.SqlServer.Configurations;

public class HumanResourceConfig : IEntityTypeConfiguration<HumanResource>
{
    public void Configure(EntityTypeBuilder<HumanResource> builder)
    {
        builder.ToTable("HumanResources");
        builder.HasKey(hr => hr.Id);
        builder.Property(hr => hr.Id)
               .HasConversion(id => id.Value, v => new FusionOps.Domain.Shared.Ids.HumanResourceId(v))
               .ValueGeneratedNever();

        builder.Property(hr => hr.FullName)
               .HasMaxLength(200)
               .IsRequired();

        // Ignore runtime-only collections
        builder.Ignore(hr => hr.DomainEvents);
        builder.Ignore(hr => hr.Skills);

        // HourRate as complex value object (struct)
        builder.ComplexProperty(hr => hr.HourRate, rate =>
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