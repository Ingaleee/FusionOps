# Local operation, migration and failure demonstration

## Initialization

The planning-only demo needs no infrastructure. The full local demo uses `docker-compose.core.yml`, whose database volumes preserve data across restarts. Ports are 1433, 5433, 5672 and 2113; change connection strings when these are already occupied. Never use the development credentials or insecure EventStore mode in a public deployment.

`Database:Initialize=true` applies migrations only in Development. Production deployments should generate and review SQL scripts, apply them through the release process, then start the application without initialization. The design-time factories generate scripts without starting the API, broker or databases:

```powershell
dotnet ef migrations script --idempotent --project FusionOps.Infrastructure --context WorkforceContext --output workforce.sql
dotnet ef migrations script --idempotent --project FusionOps.Infrastructure --context FulfillmentContext --output fulfillment.sql
```

Use EF CLI 10 with the repository's pinned runtime. Take a backup and exercise upgrades on a disposable copy before changing an existing database. `EnsureCreated` databases have no migration history and require a reviewed baseline; do not automatically run migrations against them.

The old fulfillment migration accidentally included workforce tables. The corrective migration removes their ownership from the EF model but deliberately preserves their physical data. These legacy tables require an explicit data migration/archival decision. They are never used as the workforce command store.

## Retrying a reservation after a network failure

Send `Idempotency-Key` on the first reservation request and reuse that key with the same command data for all retries. A successful retry returns 201 and the original allocation IDs. Resource order and UTC-equivalent timezone representations are canonicalized. Different command data under a completed key returns `409 idempotency_conflict`. While the original transaction is still running, a lock timeout returns `409 idempotency_in_progress` with `Retry-After: 2`.

The receipt lives in the workforce database and commits alongside state and outbox. A failed command or failed commit leaves no receipt and can be retried. Receipt keys are tenant/actor/operation scoped and currently retained indefinitely. Legacy tenant keys retain their historical identity. Do not purge these records without first defining the client retry horizon and tombstone policy; deletion can permit a historical command to execute again. See [ADR 014](adr/014-durable-reservation-idempotency.md).

## Delivery state

`Outbox` has stable Id, event Type, Payload, actor/correlation metadata and delivery state. A successful attempt sets ProcessedAt. Failures increment Attempts and record LastError; NextAttemptAt follows 2, 4, 8, 16 seconds before the fifth failure sets DeadLetteredAt. Valid messages continue while a poison record waits. SQL Server and PostgreSQL workers use separate scoped contexts and skip locked rows.

`/metrics` exports delivered, retried, dead-lettered and delivery-lag instruments from the `FusionOps.Outbox` meter. Labels contain the database store only; event IDs and user IDs are not metric dimensions. These counters do not replace querying persisted backlog/dead-letter records.

Audit insertion can succeed before a broker failure. The retry verifies the same audit stream and republishes with the original MessageId. This is why an external consumer must maintain an inbox: there is no atomic commit shared by the broker and the command database. SignalR notifications are transient delivery attempts, not durable client subscriptions.

## Approved reservation recovery

The [workflow runbook](reservation-workflows.md) describes proposals, If-Match approval, worker leases, compensation and operator resume. `examples/workflow-demo.ps1` demonstrates success and recovery against the full Development API. Workflow execution is enabled by default and can be disabled using `Workflows:WorkerEnabled=false` while retaining persisted work.

Confirmed workflow participant rejection is retained alongside successful outcomes. Ordinary HTTP allocation failures still have no cached receipt. Unknown outcomes retry their original step; NeedsAttention requires inspection and a version-checked resume. Do not turn a transport timeout into a business rejection or delete a negative participant receipt to force a new attempt.

For a containerized API, configure `Rabbit:Host`, `Rabbit:Username`, `Rabbit:Password` and the two database connection strings through deployment configuration. Development defaults target local services. Readiness includes command receipts, coordinator and participant inbox schemas. Workflow migrations preserve existing license allocations; reservation ownership is nullable for ordinary allocations. Do not roll these migrations back with active workflows or retained recovery evidence.

## Catalog and quote upgrade

The [authoritative planning contract](authoritative-planning.md) replaces arbitrary resource IDs in new workflows with quoteId. Apply SQL AuthoritativePlanningCatalog and PostgreSQL ApprovedQuoteBinding together before enabling traffic. Historical humans/equipment and anonymous allocated IDs become inactive catalog records; review metadata and activate with an ETag update. A legacy human/equipment ID collision aborts and rolls back migration instead of choosing a kind. Existing allocations, receipts and unquoted workflows remain available for recovery. Quotes are immutable commercial evidence with indefinite retention; compensation does not make a quote reusable.

Readiness checks catalog/skills, quotes, coordinator and participant tables. A deployment with only one context upgraded remains unready. A quote rejected for stale metadata, availability or expiry needs a new calculation and review; operator resume cannot override the durable decision.

## Tenant rollout

Apply both TenantOwnership migrations with administrator credentials before admitting traffic. They assign existing records and preserved PostgreSQL archives to the fixed legacy tenant, preserve historical fingerprints and install row security. Configure separate restricted application and worker principals using `sql`/`pg` and `sqlWorker`/`pgWorker`; production readiness rejects administrator credentials and unsafe role membership. Review the grants and ownership assignment in [tenant operations](tenant-isolation.md). Back up both stores together; removing tenant ownership after multi-tenant use is not a safe rollback.

Production tenant identity comes from exactly one verified JWT claim. Development-only `X-Tenant-Id` supports `examples/tenant-isolation-demo.ps1`; it cannot override an authenticated token. Outbox scans/coordinator claims use worker authority, then delivery/participants enter the persisted tenant. RabbitMQ headers, audit streams and SignalR groups carry that scope. Keep Npgsql connection reset enabled and use a fresh context per execution scope.

## Projection

The projector reads at most 100 source entries per batch and polls continuously. Allocation insertion uses `ON CONFLICT(id) DO NOTHING`. The row and checkpoint commit in one transaction. Inclusive replay is safe; optimistic checkpoint tokens reject stale workers. Invalid recognized events stop checkpoint advancement and log an error. Correct the contract/data through a reviewed operation before resuming; silently skipping an audit event is not the default.

## Failure demonstrations

1. Submit `examples/specialist-plan.json`; inspect the specialist assignment, rejected booked candidate and primal/dual equality.
2. In full mode reserve the same resource/period twice; the second response is 409. The integration suite exercises the harder simultaneous-empty-calendar case.
3. Stop only the local stack's eventstore service, commit a reservation, then inspect pending outbox state. Restart EventStore before attempts are exhausted to observe recovery. After dead-lettering, replay requires an operator operation; it is not automatic.
4. Run the reliability tests to observe injected commit failure, poison-event isolation and competing dispatchers on disposable databases.

`/health/live` tests process responsiveness. `/health/ready` includes database outbox schema checks and broker health in full mode. Audit delivery can lag independently of command acceptance; monitor persisted backlog and errors.

The legacy Docker/Helm/chaos examples require their own environment and credentials. They are not part of the verified local core startup.
