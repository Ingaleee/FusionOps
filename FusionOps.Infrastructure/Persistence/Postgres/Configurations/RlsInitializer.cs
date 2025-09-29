using Microsoft.EntityFrameworkCore;

namespace FusionOps.Infrastructure.Persistence.Postgres.Configurations;

public static class RlsInitializer
{
    public static async Task EnsureRlsAsync(FusionOps.Infrastructure.Persistence.Postgres.FulfillmentContext ctx, CancellationToken ct = default)
    {
        var sql = @"
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_settings WHERE name = 'app.tenant_id') THEN
    PERFORM set_config('app.tenant_id', '', false);
  END IF;
END $$;

ALTER TABLE IF EXISTS stock_items ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS stock_items FORCE ROW LEVEL SECURITY;
CREATE POLICY IF NOT EXISTS p_stock_items_sel ON stock_items FOR SELECT USING (current_setting('app.tenant_id', true) = tenant_id);
CREATE POLICY IF NOT EXISTS p_stock_items_ins ON stock_items FOR INSERT WITH CHECK (tenant_id = current_setting('app.tenant_id', true));
CREATE POLICY IF NOT EXISTS p_stock_items_upd ON stock_items FOR UPDATE USING (current_setting('app.tenant_id', true) = tenant_id) WITH CHECK (tenant_id = current_setting('app.tenant_id', true));
CREATE POLICY IF NOT EXISTS p_stock_items_del ON stock_items FOR DELETE USING (current_setting('app.tenant_id', true) = tenant_id);

ALTER TABLE IF EXISTS allocation_history_rows ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS allocation_history_rows FORCE ROW LEVEL SECURITY;
CREATE POLICY IF NOT EXISTS p_alloc_hist_sel ON allocation_history_rows FOR SELECT USING (current_setting('app.tenant_id', true) = tenant_id);
CREATE POLICY IF NOT EXISTS p_alloc_hist_ins ON allocation_history_rows FOR INSERT WITH CHECK (tenant_id = current_setting('app.tenant_id', true));
CREATE POLICY IF NOT EXISTS p_alloc_hist_upd ON allocation_history_rows FOR UPDATE USING (current_setting('app.tenant_id', true) = tenant_id) WITH CHECK (tenant_id = current_setting('app.tenant_id', true));
CREATE POLICY IF NOT EXISTS p_alloc_hist_del ON allocation_history_rows FOR DELETE USING (current_setting('app.tenant_id', true) = tenant_id);

CREATE INDEX IF NOT EXISTS ix_stock_items_tenant_sku ON stock_items(tenant_id, sku);
";

        await ctx.Database.ExecuteSqlRawAsync(sql, ct);
    }
}


