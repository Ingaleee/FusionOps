using FusionOps.Domain.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;
namespace FusionOps.Infrastructure.Execution;
public sealed class ExecutionTaskConfig : IEntityTypeConfiguration<ExecutionTask>
{
    public void Configure(EntityTypeBuilder<ExecutionTask> b)
    {
        b.ToTable("ops_execution_tasks"); b.HasKey(t => new { t.PlanId, t.TaskId }); b.HasAlternateKey(t => new { t.TenantId, t.PlanId, t.TaskId });
        b.Property(t => t.Version).IsConcurrencyToken(); b.Property(t => t.State).HasConversion<string>().HasMaxLength(32); b.Ignore(t => t.DomainEvents);
        b.HasOne<ExecutionActivation>().WithMany().HasForeignKey(t => new { t.TenantId, t.PlanId }).HasPrincipalKey(a => new { a.TenantId, a.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ExecutionBaseline>().WithMany().HasForeignKey(t => new { t.TenantId, t.ProjectId, t.PlanId }).HasPrincipalKey(a => new { a.TenantId, a.ProjectId, a.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class ExecutionTaskFactConfig : IEntityTypeConfiguration<ExecutionTaskFact>
{
    public void Configure(EntityTypeBuilder<ExecutionTaskFact> b)
    {
        b.ToTable("ops_execution_task_facts"); b.HasKey(f => new { f.PlanId, f.TaskId, f.CommandId });
        b.Property(f => f.Actor).HasMaxLength(200); b.Property(f => f.RequestHash).HasMaxLength(64); b.Property(f => f.RejectionCode).HasMaxLength(100);
        b.Property(f => f.State).HasConversion<string>().HasMaxLength(32); b.Property(f => f.Mutation).HasConversion<string>().HasMaxLength(32);
        b.HasOne<ExecutionTask>().WithMany().HasForeignKey(f => new { f.TenantId, f.PlanId, f.TaskId }).HasPrincipalKey(t => new { t.TenantId, t.PlanId, t.TaskId }).OnDelete(DeleteBehavior.Restrict);
    }
}
public static class ExecutionTaskSecurity
{
    public static void Install(MigrationBuilder migration)
    {
        const string allowed = "(\"TenantId\" = NULLIF(current_setting('fusionops.tenant', true), '')::uuid OR (current_setting('fusionops.system', true) = 'on' AND pg_has_role(current_user, 'fusionops_system', 'member')))";
        migration.Sql($"ALTER TABLE ops_execution_tasks ENABLE ROW LEVEL SECURITY; ALTER TABLE ops_execution_tasks FORCE ROW LEVEL SECURITY; CREATE POLICY fusionops_tenant_access ON ops_execution_tasks FOR ALL USING {allowed} WITH CHECK {allowed};");
        migration.Sql($"ALTER TABLE ops_execution_task_facts ENABLE ROW LEVEL SECURITY; ALTER TABLE ops_execution_task_facts FORCE ROW LEVEL SECURITY; CREATE POLICY fusionops_tenant_read ON ops_execution_task_facts FOR SELECT USING {allowed}; CREATE POLICY fusionops_tenant_append ON ops_execution_task_facts FOR INSERT WITH CHECK {allowed};");
    }
}
