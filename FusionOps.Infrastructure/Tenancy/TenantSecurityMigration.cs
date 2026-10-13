using Microsoft.EntityFrameworkCore.Migrations;

namespace FusionOps.Infrastructure.Tenancy;

public static class TenantSecurityMigration
{
    public static readonly string[] SqlTables = ["Allocations", "ResourceCatalog", "CatalogSkills", "HumanResources", "EquipmentResources", "PlanningQuotes", "CommandReceipts", "Outbox"];
    public static readonly string[] PgTables = ["StockItems", "Warehouses", "license_pools", "license_allocations", "reservation_workflows", "workflow_transitions", "workflow_license_requirements", "workflow_effect_inbox", "allocation_history", "Outbox", "Allocations", "HumanResources", "EquipmentResources"];

    public static void InstallSql(MigrationBuilder migration)
    {
        migration.Sql("IF DATABASE_PRINCIPAL_ID(N'fusionops_system') IS NULL CREATE ROLE [fusionops_system];");
        var function = """
            CREATE FUNCTION dbo.fn_fusionops_tenant(@TenantId uniqueidentifier)
            RETURNS TABLE WITH SCHEMABINDING AS RETURN
            SELECT 1 AS allowed WHERE
                @TenantId = TRY_CONVERT(uniqueidentifier, SESSION_CONTEXT(N'fusionops.tenant'))
                OR (CONVERT(nvarchar(3), SESSION_CONTEXT(N'fusionops.system')) = N'on'
                    AND (IS_MEMBER(N'fusionops_system') = 1 OR IS_MEMBER(N'db_owner') = 1));
            """;
        // Dynamic DDL also works inside EF's reviewed idempotent migration scripts.
        migration.Sql("EXEC(N'" + function.Replace("'", "''") + "');");
        var predicates = SqlTables.SelectMany(t => new[]
        {
            $"ADD FILTER PREDICATE dbo.fn_fusionops_tenant(TenantId) ON dbo.[{t}]",
            $"ADD BLOCK PREDICATE dbo.fn_fusionops_tenant(TenantId) ON dbo.[{t}] AFTER INSERT",
            $"ADD BLOCK PREDICATE dbo.fn_fusionops_tenant(TenantId) ON dbo.[{t}] AFTER UPDATE"
        });
        migration.Sql("CREATE SECURITY POLICY dbo.fusionops_tenant_policy " + string.Join(",\n", predicates) + " WITH (STATE = ON, SCHEMABINDING = ON);");
    }
    public static void RemoveSql(MigrationBuilder migration)
    {
        migration.Sql("DROP SECURITY POLICY dbo.fusionops_tenant_policy; DROP FUNCTION dbo.fn_fusionops_tenant;");
    }
    public static void InstallPostgres(MigrationBuilder migration)
    {
        foreach (var legacy in new[] { "Allocations", "HumanResources", "EquipmentResources" })
            migration.Sql($"ALTER TABLE \"{legacy}\" ADD COLUMN \"TenantId\" uuid NOT NULL DEFAULT '11111111-1111-1111-1111-111111111111';");
        migration.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'fusionops_system') THEN CREATE ROLE fusionops_system NOLOGIN; END IF; END $$;");
        const string expression = "(\"TenantId\" = NULLIF(current_setting('fusionops.tenant', true), '')::uuid OR (current_setting('fusionops.system', true) = 'on' AND pg_has_role(current_user, 'fusionops_system', 'member')))";
        foreach (var table in PgTables)
            migration.Sql($"ALTER TABLE \"{table}\" ENABLE ROW LEVEL SECURITY; ALTER TABLE \"{table}\" FORCE ROW LEVEL SECURITY; CREATE POLICY fusionops_tenant ON \"{table}\" USING {expression} WITH CHECK {expression};");
    }
    public static void RemovePostgres(MigrationBuilder migration)
    {
        foreach (var table in PgTables)
            migration.Sql($"DROP POLICY fusionops_tenant ON \"{table}\"; ALTER TABLE \"{table}\" NO FORCE ROW LEVEL SECURITY; ALTER TABLE \"{table}\" DISABLE ROW LEVEL SECURITY;");
        foreach (var legacy in new[] { "Allocations", "HumanResources", "EquipmentResources" })
            migration.Sql($"ALTER TABLE \"{legacy}\" DROP COLUMN \"TenantId\";");
    }
}
