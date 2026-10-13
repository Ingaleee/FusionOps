# ADR 012: local transactions and explicit delivery semantics

Status: accepted. Date: 2026-10-12.

## Decision

A write request declares Workforce (SQL Server) or Fulfillment (PostgreSQL). The command pipeline opens a transaction before executing its handler and commits once. Queries bypass this behavior. The unit of work rejects changes in the other context before any SaveChanges call.

Workforce reservations use a serializable transaction, `UPDLOCK, HOLDLOCK` and an index on ResourceId. The key-range lock covers a resource whose calendar is empty. Multi-resource commands acquire locks in sorted ID order. Half-open UTC intervals allow adjacent reservations. Cancelled allocations are excluded from conflict checks.

The immutable reservation period is mapped as an EF complex value, so one request can share it across multiple allocations without owned-entity identity conflicts. Calendar locking reads scalar IDs; a subsequent LINQ query materializes values with their configured physical column names under the same retained locks. The migration changes tracking metadata without rewriting period columns.

State and serialized events commit in the same database. Events retain their original ID and timestamp, and carry actor and correlation metadata. Events are cleared only after commit. License pools use an application-controlled concurrency token to detect lost updates.

Each outbox worker locks one pending row using SQL Server `READPAST` or PostgreSQL `SKIP LOCKED`. It holds that lock while performing a delivery attempt bounded to ten seconds, then commits success or durable backoff/dead-letter state. Two stores run in independent scopes. A poison event does not roll back previous deliveries or starve eligible rows. The current defaults are 25 rows per store per polling cycle and five failed attempts before dead-lettering.

The audit stream `audit-event-{eventId}` is append-only with expected state NoStream. A retry verifies the stored event ID and content. Broker publication uses the same MessageId on every retry. Projection insertion uses the audit event ID as its primary key and commits its checkpoint in the same PostgreSQL transaction; replay therefore cannot insert the same row twice. Optimistic checkpoint concurrency prevents stale workers moving the checkpoint backwards.

## Guarantees and limits

- Atomic state + outbox within one database.
- Durable at-least-once delivery attempts; duplicates remain possible between external side effects and the outbox commit.
- Audit insertion is idempotent for a stable event identity and identical content.
- Projection rows are idempotent; a failed projection cannot advance its checkpoint past the failed event.
- No distributed transaction between SQL Server, PostgreSQL, EventStoreDB and RabbitMQ.
- Consumers must implement their own inbox/idempotency. SignalR does not guarantee delivery to disconnected clients.
- Dead-letter records require operator action. A replay API and retention policy are future milestones.
- Direct SQL writers bypass the application reservation protocol. Production must restrict table writes to the owning service; a database-wide exclusion mechanism is a separate hardening milestone.
- Resource identity is caller-managed in this increment; reservation does not establish catalog existence or tenant ownership. Catalog/tenant enforcement must precede a public multi-tenant deployment.
- Locks held across network delivery bound concurrency and consume a connection. A lease-based dispatcher is a future option if measurement justifies the extra recovery/fencing complexity.

## Alternatives

Saving both DbContexts under one SQL Server transaction was rejected: PostgreSQL was not enlisted and partial commits were possible. Clearing events in an audit middleware was rejected because an external failure could lose events before their state was committed. Publishing during the HTTP request was rejected because broker downtime would unnecessarily couple command availability to integration delivery.

References: [EF Core transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions), [EF Core concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency).
