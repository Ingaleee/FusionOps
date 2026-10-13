using Microsoft.EntityFrameworkCore.Migrations;

namespace FusionOps.Infrastructure.Projects;

public static class ProjectSecurityMigration
{
    public static void Install(MigrationBuilder migration)
    {
        const string allowed = "(\"TenantId\" = NULLIF(current_setting('fusionops.tenant', true), '')::uuid OR (current_setting('fusionops.system', true) = 'on' AND pg_has_role(current_user, 'fusionops_system', 'member')))";
        foreach (var table in new[] { "ops_projects", "ops_project_members" })
            migration.Sql($"ALTER TABLE \"{table}\" ENABLE ROW LEVEL SECURITY; ALTER TABLE \"{table}\" FORCE ROW LEVEL SECURITY; CREATE POLICY fusionops_tenant ON \"{table}\" USING {allowed} WITH CHECK {allowed};");
        // Runtime users can read/append evidence, but have no policy allowing update or deletion.
        migration.Sql($"ALTER TABLE ops_project_revisions ENABLE ROW LEVEL SECURITY; ALTER TABLE ops_project_revisions FORCE ROW LEVEL SECURITY; CREATE POLICY fusionops_tenant_read ON ops_project_revisions FOR SELECT USING {allowed}; CREATE POLICY fusionops_tenant_append ON ops_project_revisions FOR INSERT WITH CHECK {allowed};");
    }
}
