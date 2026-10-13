using FusionOps.Domain.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FusionOps.Infrastructure.Execution;

public sealed class TemporalLicenseEffectReceiptConfig : IEntityTypeConfiguration<TemporalLicenseEffectReceipt>
{
    public void Configure(EntityTypeBuilder<TemporalLicenseEffectReceipt> b)
    {
        b.ToTable("execution_license_effects"); b.HasKey(r => new { r.PlanId, r.Step });
        b.Property(r => r.Step).HasMaxLength(32); b.Property(r => r.Digest).HasMaxLength(64); b.Property(r => r.RejectionCode).HasMaxLength(100);
        b.HasOne<ExecutionBaseline>().WithMany().HasForeignKey(r => new { r.TenantId, r.ProjectId, r.PlanId })
            .HasPrincipalKey(baseline => new { baseline.TenantId, baseline.ProjectId, baseline.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
