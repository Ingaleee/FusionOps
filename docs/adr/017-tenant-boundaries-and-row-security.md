# ADR 017: Tenant execution, row security and worker authority

Status: accepted. Date: 2026-10-13.

## Problem

The shared catalog introduced authoritative economics, but resource, quote, workflow, license, stock, receipt and history access had no tenant boundary. Global notification rooms, natural-key uniqueness, raw SQL and audit metadata were additional paths around a simple repository filter.

## Decision

Production requests derive one nonempty tenant_id and one bounded subject from a verified JWT. Role authorization still applies. Request headers cannot replace verified claims. Development permits a documented X-Tenant-Id header and a fixed legacy tenant; this mechanism is unavailable to production callers.

Execution scope flows through async work, while each DbContext captures one scope and seals it on first connection use. Global EF filters and immutable tenant ownership guard tracked reads/writes. Foreign resource allocation references use a composite tenant/resource foreign key; owned skills, workflow history/requirements and license references carry the owner's tenant through composite foreign keys. Product names and SKUs are unique per tenant. Globally unique GUID identities remain globally unique.

Database policies enforce the same scope for unfiltered and raw reads/writes. SQL Server uses a schema-bound predicate with filter and after-insert/update block predicates. Its session tenant/system values are read-only for a checked-out connection. PostgreSQL enables and forces row security with USING/WITH CHECK policies. Connection-open hooks reinitialize scope on every checkout. Existing archived workforce tables in PostgreSQL receive policies as well.

SQL calendar locking and materialization seek the explicit tenant/resource index in one query. On a sparse table the optimizer can choose a clustered scan despite a tenant predicate, taking shared serializable primary-key ranges that deadlock independent inserts. Quote calendar reads use the same seek requirement. The index name is part of this SQL protocol and must be retained or updated with these queries in any schema change.

Global access additionally requires membership in fusionops_system. Application database principals must be restricted and cannot acquire global rights by setting a system session flag. Worker connection strings are separate; production readiness checks both application and worker principal privileges. Superuser/BYPASSRLS/database owner credentials are unsuitable production application credentials. Development bootstrap and migrations use administrator credentials explicitly.

The coordinator and outbox claim globally under worker authority. A workflow participant then opens new application connections scoped to the persisted workflow tenant. Completion is fenced by tenant, ID, step, token and epoch. External delivery carries the outbox tenant into audit, RabbitMQ headers and notification groups. The projector uses trusted global authority to write tenant-owned history alongside its global source checkpoint.

## Compatibility

Existing data, legacy audit streams and immutable evidence belong to tenant 11111111-1111-1111-1111-111111111111. Preserve legacy quote/workflow digests and receipt key hashes rather than invalidating recovery. New tenants enter those identities explicitly. Old unquoted workflows remain recoverable. No automatic ownership redistribution is inferred from actors or arbitrary project IDs.

## Limits

Row policies protect forgotten predicates, unsafe query paths and accidental cross-tenant writes. The shared application database credential can establish a tenant context and remains a trusted server secret; these policies do not isolate a tenant from an attacker who controls that credential or the database administrator. IdP tenant assignment and system-worker credentials are trusted administrative capabilities.

This milestone scopes project IDs through tenant-owned rows. It does not implement project membership/ACLs, tenant provisioning, per-tenant encryption, regional placement or measured noisy-neighbor controls. The optional GraphQL notification bridge has a single configured tenant/service identity and tenant-specific topics; fleet-wide fanout remains future work.

## Evidence

Signed JWT tests cover missing/ambiguous claims, attempted header override and simultaneous tenants. Real database tests cover HTTP access to foreign resources/quotes, scoped natural keys, scoped retries, global worker recovery, foreign tracked writes, raw SQL/RLS, limited PostgreSQL principals, connection reuse, tenant-owned audit replay and broker tenant metadata. See the verification record for completed runs.

## References

- [EF Core query filters](https://learn.microsoft.com/en-us/ef/core/querying/filters)
- [SQL Server row security and SESSION_CONTEXT](https://learn.microsoft.com/en-us/sql/relational-databases/security/row-level-security)
- [PostgreSQL row security](https://www.postgresql.org/docs/16/ddl-rowsecurity.html)
