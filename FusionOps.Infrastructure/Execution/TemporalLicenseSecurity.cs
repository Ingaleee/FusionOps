using Microsoft.EntityFrameworkCore.Migrations;

namespace FusionOps.Infrastructure.Execution;

public static class TemporalLicenseSecurity
{
    public static void Install(MigrationBuilder migration)
    {
        const string allowed = "(\"TenantId\" = NULLIF(current_setting('fusionops.tenant', true), '')::uuid OR (current_setting('fusionops.system', true) = 'on' AND pg_has_role(current_user, 'fusionops_system', 'member')))";
        migration.Sql($"ALTER TABLE execution_license_effects ENABLE ROW LEVEL SECURITY; ALTER TABLE execution_license_effects FORCE ROW LEVEL SECURITY; CREATE POLICY fusionops_tenant_read ON execution_license_effects FOR SELECT USING {allowed}; CREATE POLICY fusionops_tenant_append ON execution_license_effects FOR INSERT WITH CHECK {allowed};");
        migration.Sql($"ALTER TABLE license_temporal_reservations ENABLE ROW LEVEL SECURITY; ALTER TABLE license_temporal_reservations FORCE ROW LEVEL SECURITY; CREATE POLICY fusionops_tenant_access ON license_temporal_reservations FOR ALL USING {allowed} WITH CHECK {allowed};");
    }
}
