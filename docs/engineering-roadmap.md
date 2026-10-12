# FusionOps: from technology demo to an evidence-driven operations platform

Audit date: 2026-10-12. Baseline: GitHub `main`, before this working change.

## Product direction

FusionOps plans and reserves scarce people, machines, inventory and licenses. Its flagship demonstration should answer a difficult operational question: **what is the cheapest feasible assignment, why is it feasible, and what happens if infrastructure fails during reservation?**

Architecture quality is demonstrated through explicit guarantees, measured tradeoffs, executable failure scenarios and understandable APIs. Feature count alone is not a quality metric.

## Findings from the source audit

| Finding | Consequence | Priority |
| --- | --- | --- |
| EventStoreAuditWriter serializes and discards every envelope | Claimed audit durability is absent | P0 |
| Unit of work never creates outbox records | Successful writes produce no durable integration events | P0 |
| Reservation checks precede BeginTransaction; handler and pipeline both commit | Locks do not protect the full decision; duplicate commit ownership | P0 |
| SQL transaction also saves an unrelated PostgreSQL context | Partial cross-database writes can survive a failure | P0 |
| FulfillmentContext scans configurations for both providers | Unrelated workforce tables enter the fulfillment model | P0 |
| Main solution excludes Gateway, Projector and application tests | A green solution build does not validate the whole deliverable | P0 |
| CI is commented out; integration suite includes placeholders/skips | No independent evidence for core guarantees | P0 |
| HungarianOptimizerStrategy sorts rates | No assignment algorithm or resource-to-task constraints | P1 |
| ScenarioRunner concurrently queries shared DbContexts and calculates only first-resource costs | Scenario results are unreliable and may fail at runtime | P1 |
| License references are a HashSet but repeated allocations still increment counters | Repeated allocations can make ownership and totals disagree | P1 |
| Several policies, endpoints and notification dependencies are never registered | Features exist in source but cannot be exercised | P1 |

## Delivery order and acceptance criteria

Current delivered increment: reproducible .NET 10 foundation, transactional reservations/outboxes, explainable assignment, replayable audit and durable reservation retries. The verification record contains 52 fast tests, 17 database/broker/audit scenarios and a running-container planning check. The next milestones below remain separately scoped work.

### 1. Reproducible engineering foundation

Use .NET 10 LTS, align EF/provider versions, remove unused vulnerable mapping and legacy telemetry dependencies, restore from an explicit public NuGet source, include executable services and real tests in the solution. CI builds Release, runs fast tests and separately runs database failure/concurrency checks. Every advertised command must execute from a clean clone.

### 2. Transaction and event delivery correctness

One command owns one database boundary; its transaction begins before reads and ends after state + outbox persist. Cross-boundary writes fail explicitly and must be coordinated by a workflow with compensation. Read queries do not open write transactions. Reservation locks cover empty resource calendars, acquire resources in consistent order, and cancelled bookings do not block future reservations.

Store original event identity and type, actor and correlation ID. Dispatch both database outboxes with concurrent-worker locking, bounded per-message delivery, durable retry/backoff and dead-letter state. Audit, broker and notification delivery are at-least-once. Never claim distributed exactly-once delivery. A poison event must not block valid messages. Domain events are cleared only after commit succeeds.

### 3. Explainable resource assignment

Implement a real rectangular Hungarian solver with forbidden edges. Plan named demand slots against candidates, considering resource kind, required skills, availability, hourly rates and a common currency. Return optimal cost, assignments, unfilled slots and rejection reasons. Planning is side-effect free. Verify small random matrices against exhaustive search; include a scenario where greedy selection loses.

### 4. Portfolio demonstration

Provide a dependency-light demo, request examples, architecture diagrams and ADRs explaining guarantees and limitations. Include database tests for simultaneous empty-calendar reservations, rollback, outbox isolation and retries. Keep claimed benchmark numbers out of the README until measured.

### 5. Next product increments

1. Tenant isolation enforced at API, persistence and message boundaries; adversarial isolation tests.
2. Durable idempotency keys tied atomically to reservation responses and command fingerprints: delivered for Workforce allocation; SQL concurrency, batch rollback and HTTP replay/Location scenarios pass in the reliability suite.
3. A complete reservation-to-fulfillment saga with timeouts, compensation, inbox deduplication and operator replay.
4. Checkpointed audit projection, deterministic replay, event version migration and lag/readiness metrics.
5. Versioned planning snapshots, approval/apply flow, overtime and procurement optimization.
6. Performance report with dataset, hardware, throughput, p95/p99 and failure-recovery traces; capacity model and SLOs.
7. Minimal operations UI that makes plan explanations, audit trails and workflow recovery visible.

Each increment must ship with runnable acceptance scenarios. Except for the explicitly marked Workforce idempotency increment, these are future milestones, not current capabilities.
