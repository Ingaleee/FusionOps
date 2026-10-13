using FusionOps.Domain.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FusionOps.Infrastructure.Execution;

public sealed class ExecutionActivationConfig : IEntityTypeConfiguration<ExecutionActivation>
{
    public const string LiveStates = "\"State\" IN ('ReservingResources','ReservingLicenses','Active','ReleasingLicenses','ReleasingResources','NeedsAttention')";
    public void Configure(EntityTypeBuilder<ExecutionActivation> b)
    {
        b.ToTable("ops_execution_activations"); b.HasKey(a => a.Id); b.Property(a => a.Id).ValueGeneratedNever(); b.HasAlternateKey(a => new { a.TenantId, a.Id });
        b.Property(a => a.Version).IsConcurrencyToken(); b.Property(a => a.State).HasConversion<string>().HasMaxLength(32); b.Property(a => a.PausedStep).HasConversion<string>().HasMaxLength(32);
        b.Property(a => a.Digest).HasMaxLength(64); b.Property(a => a.RequestedBy).HasMaxLength(200); b.Property(a => a.FailureCode).HasMaxLength(100); b.Property(a => a.LastErrorCode).HasMaxLength(100);
        b.Ignore(a => a.DomainEvents); b.Ignore(a => a.ResourceOwnership);
        b.Property<Guid?>("LeaseToken"); b.Property<long>("LeaseEpoch"); b.Property<DateTimeOffset?>("LeaseExpiresAt");
        b.HasIndex(a => new { a.TenantId, a.ProjectId }).IsUnique().HasFilter(LiveStates);
        b.HasIndex(nameof(ExecutionActivation.State), nameof(ExecutionActivation.NextAttemptAt), "LeaseExpiresAt");
        b.HasOne(a => a.Baseline).WithMany().HasForeignKey(a => new { a.TenantId, a.ProjectId, a.Id })
            .HasPrincipalKey(baseline => new { baseline.TenantId, baseline.ProjectId, baseline.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class ExecutionActivationDecisionConfig : IEntityTypeConfiguration<ExecutionActivationDecision>
{
    public void Configure(EntityTypeBuilder<ExecutionActivationDecision> b)
    {
        b.ToTable("ops_execution_activation_decisions"); b.HasKey(d => new { d.ActivationId, d.Version }); b.Property(d => d.Version).ValueGeneratedNever();
        b.Property(d => d.State).HasConversion<string>().HasMaxLength(32); b.Property(d => d.PausedStep).HasConversion<string>().HasMaxLength(32);
        b.Property(d => d.Actor).HasMaxLength(200); b.Property(d => d.Reason).HasMaxLength(100); b.Property(d => d.ErrorCode).HasMaxLength(100);
        b.HasOne<ExecutionActivation>().WithMany().HasForeignKey(d => new { d.TenantId, d.ActivationId }).HasPrincipalKey(a => new { a.TenantId, a.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
public static class ExecutionActivationSecurity
{
    public static void Install(MigrationBuilder migration)
    {
        const string allowed = "(\"TenantId\" = NULLIF(current_setting('fusionops.tenant', true), '')::uuid OR (current_setting('fusionops.system', true) = 'on' AND pg_has_role(current_user, 'fusionops_system', 'member')))";
        migration.Sql($"ALTER TABLE ops_execution_activations ENABLE ROW LEVEL SECURITY; ALTER TABLE ops_execution_activations FORCE ROW LEVEL SECURITY; CREATE POLICY fusionops_tenant_access ON ops_execution_activations FOR ALL USING {allowed} WITH CHECK {allowed};");
        migration.Sql($"ALTER TABLE ops_execution_activation_decisions ENABLE ROW LEVEL SECURITY; ALTER TABLE ops_execution_activation_decisions FORCE ROW LEVEL SECURITY; CREATE POLICY fusionops_tenant_read ON ops_execution_activation_decisions FOR SELECT USING {allowed}; CREATE POLICY fusionops_tenant_append ON ops_execution_activation_decisions FOR INSERT WITH CHECK {allowed};");
    }
}
