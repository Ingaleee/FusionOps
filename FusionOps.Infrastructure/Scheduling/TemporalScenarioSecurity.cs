using Microsoft.EntityFrameworkCore.Migrations;

namespace FusionOps.Infrastructure.Scheduling;

public static class TemporalScenarioSecurity
{
    public static void Install(MigrationBuilder migration)
    {
        const string allowed = "(\"TenantId\" = NULLIF(current_setting('fusionops.tenant', true), '')::uuid OR (current_setting('fusionops.system', true) = 'on' AND pg_has_role(current_user, 'fusionops_system', 'member')))";
        migration.Sql($"ALTER TABLE ops_temporal_scenarios ENABLE ROW LEVEL SECURITY; ALTER TABLE ops_temporal_scenarios FORCE ROW LEVEL SECURITY; CREATE POLICY fusionops_tenant_read ON ops_temporal_scenarios FOR SELECT USING {allowed}; CREATE POLICY fusionops_tenant_append ON ops_temporal_scenarios FOR INSERT WITH CHECK {allowed};");
    }
}
