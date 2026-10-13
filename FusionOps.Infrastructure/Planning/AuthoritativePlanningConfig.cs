using FusionOps.Domain.Catalog;
using FusionOps.Domain.Services.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FusionOps.Infrastructure.Planning;

public sealed class CatalogResourceConfig : IEntityTypeConfiguration<CatalogResource>
{
    public void Configure(EntityTypeBuilder<CatalogResource> b)
    {
        b.ToTable("ResourceCatalog");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.Name).HasMaxLength(200);
        b.Property(r => r.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(r => r.EquipmentType).HasMaxLength(64);
        b.Property(r => r.Revision).IsConcurrencyToken();
        b.Ignore(r => r.DomainEvents);
        b.ComplexProperty(r => r.HourRate, rate =>
        {
            rate.Property(m => m.Amount).HasColumnName("HourRateAmount").HasPrecision(20, 6);
            rate.Property(m => m.Currency).HasColumnName("HourRateCurrency").HasConversion(c => c.Value, c => FusionOps.Domain.Enumerations.Currency.FromValue(c));
        });
        b.OwnsMany(r => r.Skills, s =>
        {
            s.ToTable("CatalogSkills");
            s.WithOwner().HasForeignKey("TenantId", "ResourceId").HasPrincipalKey(nameof(CatalogResource.TenantId), nameof(CatalogResource.Id));
            s.Property(v => v.Name).HasMaxLength(64);
            s.HasKey("ResourceId", nameof(CatalogSkill.Name));
        });
    }
}

public sealed class PlanningQuoteConfig : IEntityTypeConfiguration<PlanningQuote>
{
    public void Configure(EntityTypeBuilder<PlanningQuote> b)
    {
        b.ToTable("PlanningQuotes");
        b.HasKey(q => q.Id);
        b.Property(q => q.Id).ValueGeneratedNever();
        b.Property(q => q.CreatedBy).HasMaxLength(200);
        b.Property(q => q.Digest).HasMaxLength(64);
        b.Property(q => q.ConsumedByWorkflowId).IsConcurrencyToken();
        b.HasIndex(q => q.ExpiresAt);
        b.Ignore(q => q.Evidence);
        b.Ignore(q => q.Binding);
    }
}
