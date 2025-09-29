## Multi-tenancy in FusionOps

Baseline: PostgreSQL Row-Level Security (RLS) with shadow `TenantId` and global EF filters. Optional: Schema-per-tenant for VIP customers.

### Resolution
- Middleware `TenantResolutionMiddleware` resolves tenant from JWT `tenant_id`, subdomain, or `X-Tenant-Id` header.
- `TenantProvider` exposes the current tenant for EF, messaging, and metrics.

### EF & Persistence
- Shadow property `TenantId` on all entities; set on inserts in DbContext(s).
- Global query filter equals `EF.Property<string>(e, "TenantId") == tenant`.
- Npgsql connection interceptor sets `SET app.tenant_id = '{tenant}'` to activate RLS policies.

### RLS Policies
Policies are applied in `RlsInitializer` for Postgres tables (e.g. `stock_items`, `allocation_history_rows`). Ensure app DB user does not have `BYPASSRLS`.

### Messaging
- Rabbit/Kafka include header `tenant_id` for consumers and sagas.
- Outbox dispatched events carry headers via bus publish context.

### Authorization
- Policies use claim `tenant_roles` from Keycloak mappers (e.g. `T:AdminStock`).

### Schema-per-tenant (optional)
Enable with `MultiTenancy:Mode = Schema`.
- EF model cache key includes tenant id.
- Transaction interceptor sets `SET LOCAL search_path = t_{tenant}, public`.
- Onboarding endpoint `POST /ops/tenants/{slug}` creates schema `t_{slug}`.

### Configuration
```json
{
  "MultiTenancy": {
    "Mode": "Rls" // or "Schema"
  }
}
```

### Testing
`RlsIsolationTests` (Testcontainers) inserts under tenant A and verifies tenant B can't read those rows.


