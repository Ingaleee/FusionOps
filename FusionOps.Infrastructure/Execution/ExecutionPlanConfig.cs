using FusionOps.Domain.Execution;
using FusionOps.Domain.Projects;
using FusionOps.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FusionOps.Infrastructure.Execution;

public sealed class ExecutionBaselineConfig : IEntityTypeConfiguration<ExecutionBaseline>
{
    public void Configure(EntityTypeBuilder<ExecutionBaseline> b)
    {
        b.ToTable("ops_execution_baselines"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.CreatedBy).HasMaxLength(200); b.Property(x => x.RequestHash).HasMaxLength(64);
        b.Property(x => x.Digest).HasMaxLength(64); b.Property(x => x.DefinitionDigest).HasMaxLength(64); b.Property(x => x.Currency).HasMaxLength(3);
        b.Ignore(x => x.Evidence); b.Ignore(x => x.ScenarioEvidence);
        b.HasAlternateKey(x => new { x.TenantId, x.ProjectId, x.Id });
        b.HasOne<TemporalScenario>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProjectId, x.ScenarioId })
            .HasPrincipalKey(s => new { s.TenantId, s.ProjectId, s.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProjectDefinitionRevision>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProjectId, x.DefinitionVersion })
            .HasPrincipalKey(r => new { r.TenantId, r.ProjectId, r.DefinitionVersion }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ExecutionBaseline>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProjectId, x.PreviousPlanId })
            .HasPrincipalKey(x => new { x.TenantId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class ExecutionPlanConfig : IEntityTypeConfiguration<ExecutionPlan>
{
    public void Configure(EntityTypeBuilder<ExecutionPlan> b)
    {
        b.ToTable("ops_execution_plans"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Version).IsConcurrencyToken(); b.Property(x => x.State).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.ApprovedBy).HasMaxLength(200); b.Ignore(x => x.DomainEvents);
        b.HasIndex(x => new { x.TenantId, x.ProjectId, x.Id });
        b.HasOne(x => x.Baseline).WithOne().HasForeignKey<ExecutionPlan>(x => new { x.TenantId, x.ProjectId, x.Id })
            .HasPrincipalKey<ExecutionBaseline>(x => new { x.TenantId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class ExecutionPlanDecisionConfig : IEntityTypeConfiguration<ExecutionPlanDecision>
{
    public void Configure(EntityTypeBuilder<ExecutionPlanDecision> b)
    {
        b.ToTable("ops_execution_plan_decisions"); b.HasKey(x => new { x.PlanId, x.Version });
        b.Property(x => x.Actor).HasMaxLength(200); b.Property(x => x.Action).HasMaxLength(32); b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.ReviewedDigest).HasMaxLength(64); b.Property(x => x.PolicyVersion).HasMaxLength(64);
        b.Property(x => x.State).HasConversion<string>().HasMaxLength(32);
        b.HasOne<ExecutionPlan>().WithMany().HasForeignKey(x => new { x.TenantId, x.PlanId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
