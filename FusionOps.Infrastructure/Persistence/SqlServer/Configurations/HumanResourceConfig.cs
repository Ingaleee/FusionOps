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

        builder.Property(hr => hr.FullName)
               .HasMaxLength(200)
               .IsRequired();

        // Ignore runtime-only collections
        builder.Ignore(hr => hr.DomainEvents);
        builder.Ignore(hr => hr.Skills);

        // HourRate as owned value object (default column names)
        builder.Ignore(hr => hr.HourRate);
    }
}