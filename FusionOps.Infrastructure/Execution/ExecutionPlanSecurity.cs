using Microsoft.EntityFrameworkCore.Migrations;

namespace FusionOps.Infrastructure.Execution;

public static class ExecutionPlanSecurity
{
    public static void Install(MigrationBuilder migration)
    {
        const string allowed = "(\"TenantId\" = NULLIF(current_setting('fusionops.tenant', true), '')::uuid OR (current_setting('fusionops.system', true) = 'on' AND pg_has_role(current_user, 'fusionops_system', 'member')))";
        foreach (var table in new[] { "ops_execution_baselines", "ops_execution_plan_decisions" })
            migration.Sql($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY; ALTER TABLE {table} FORCE ROW LEVEL SECURITY; CREATE POLICY fusionops_tenant_read ON {table} FOR SELECT USING {allowed}; CREATE POLICY fusionops_tenant_append ON {table} FOR INSERT WITH CHECK {allowed};");
        migration.Sql($"ALTER TABLE ops_execution_plans ENABLE ROW LEVEL SECURITY; ALTER TABLE ops_execution_plans FORCE ROW LEVEL SECURITY; CREATE POLICY fusionops_tenant_access ON ops_execution_plans FOR ALL USING {allowed} WITH CHECK {allowed};");
    }
}
