using FusionOps.Domain.Projects;
using FusionOps.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FusionOps.Infrastructure.Scheduling;

public sealed class TemporalScenarioConfig : IEntityTypeConfiguration<TemporalScenario>
{
    public void Configure(EntityTypeBuilder<TemporalScenario> b)
    {
        b.ToTable("ops_temporal_scenarios"); b.HasKey(s => s.Id); b.Property(s => s.Id).ValueGeneratedNever();
        b.Property(s => s.Actor).HasMaxLength(200); b.Property(s => s.RequestHash).HasMaxLength(64); b.Property(s => s.Digest).HasMaxLength(64);
        b.Ignore(s => s.Evidence); b.Ignore(s => s.DomainEvents);
        b.HasIndex(s => new { s.TenantId, s.ProjectId, s.Id });
        b.HasOne<ProjectDefinitionRevision>().WithMany().HasForeignKey(s => new { s.TenantId, s.ProjectId, s.DefinitionVersion })
            .HasPrincipalKey(r => new { r.TenantId, r.ProjectId, r.DefinitionVersion }).OnDelete(DeleteBehavior.Restrict);
    }
}
