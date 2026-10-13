# Tenant isolation and database authority

## Request contract

Production data endpoints require a JWT verified for the configured issuer/audience, exactly one nonempty tenant_id GUID and one bounded subject (sub, mapped to NameIdentifier). Manager/audit role policies still apply. Missing, invalid or duplicate tenant/subject claims fail before database access. X-Tenant-Id cannot override a verified token and returns 403.

Development anonymous requests use tenant 11111111-1111-1111-1111-111111111111. The optional Development-only X-Tenant-Id GUID supports local two-tenant demonstrations. Configure a trusted identity-provider mapper for tenant assignment; never infer it from an unsigned payload or caller-selected project ID.

Resource, quote, project and workflow GUIDs remain globally unique. Foreign reads/mutations find no owned record; foreign resources/quotes cannot be reserved. Registered projects add explicit membership and operation permissions in addition to tenant ownership; see [project authority](project-commitments.md). Production requires reviewed registered projects; Development retains unmanaged IDs for legacy examples.

## Persistence

A context captures one execution scope and seals it on first connection use. EF filters scope reads; tracked writes require the same immutable owner. Missing scope forbids database access. Product/SKU uniqueness includes tenant. Composite foreign keys keep allocations, skills, license references and workflow requirements/history in their parent's tenant.

SQL Server policies filter reads and block foreign inserts/updates. PostgreSQL enables and forces USING/WITH CHECK policies, including preserved legacy workforce tables. Global checkpoints and migration metadata are infrastructure state. Removing an EF filter does not remove the database policy.

Connection-open hooks initialize tenant/system flags on each checkout. SQL flags are read-only until return to the pool; PostgreSQL connection reset clears custom settings. Do not set Npgsql No Reset On Close, share an open connection between tenant contexts or pool DbContexts across request scopes.

## Runtime principals

Apply migrations with an administrator connection. Configure separate secrets:

| Connection string | Principal |
| --- | --- |
| sql / pg | Restricted application login |
| sqlWorker / pgWorker | Restricted worker login, member of fusionops_system |

PostgreSQL runtime logins must have NOSUPERUSER and NOBYPASSRLS. SQL runtime logins must not be sysadmin/database owner. The application principal must not belong to fusionops_system. Production readiness checks these conditions and worker membership. Development defaults retain administrator credentials solely for bootstrap convenience.

Administrators create logins/users with separately managed passwords. In SQL Server grant db_datareader/db_datawriter to both runtime users and fusionops_system only to the worker. In PostgreSQL grant schema USAGE, table SELECT/INSERT/UPDATE/DELETE and sequence USAGE/SELECT to both users; grant fusionops_system only to the worker. Apply reviewed grants to future tables. Keep role creation, schema changes, TRUNCATE and policy administration privileges away from runtime users.

The system session flag alone cannot give an application principal global access: row policies also check database role membership. The shared application credential can establish a tenant context and remains a trusted server secret. These policies do not protect against credential theft, database administrators or compromised IdP tenant assignment.

## Recovery and delivery

Coordinator claims/outbox scans use global worker authority. Participants open fresh application connections for the persisted workflow tenant. Completion checks tenant with lease token/epoch and step. Actors/idempotency keys can repeat in different tenants without cross-tenant response replay.

Outbox TenantId enters the audit envelope and tenant-specific EventStore stream, fusionops-tenant-id RabbitMQ header and tenant role groups in SignalR. The fixed legacy tenant retains its original audit stream and JSON shape, including omission of the new optional tenant field; an append completed before upgrade can resolve the same event after retry. Consumers must validate message metadata before entering a scope; a general broker inbox remains future work.

The projector verifies envelope/stream tenant and original event UUID before writing scoped history alongside its global checkpoint. GraphQL stock subscriptions require a verified tenant and Stock.Admin. The optional single-tenant bridge checks that its configured topic matches its service token; the upstream API verifies the token signature. Multi-tenant bridge orchestration and token refresh remain future work.

## Upgrade

Both TenantOwnership migrations assign existing data and PostgreSQL archives to the fixed legacy tenant, add composite ownership and install policies. Legacy quote/workflow fingerprints, receipt hashes and audit streams remain valid for recovery. New tenants enter those identities explicitly.

This is one documented ownership assignment, not inference of original business ownership. Redistribution requires a reviewed migration across both stores, receipts and audit. Downgrade is unsuitable after multiple tenants have data: removing scope can collide natural keys and discard recovery/security evidence.

See [ADR 017](adr/017-tenant-boundaries-and-row-security.md), [planning contract](authoritative-planning.md) and [verification](verification.md).
