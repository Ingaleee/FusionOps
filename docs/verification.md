# Verification record

Updated: 2026-10-13. Local environment: Windows x64, .NET SDK 10.0.303 / runtime 10.0.12.

| Check | Result |
| --- | --- |
| Release solution build with warnings as errors | Pass, 0 warnings, 0 errors; 11 projects |
| Fast tests | 83 pass: 48 domain/algorithm/contract/architecture/workflow/quote tests and 35 application/HTTP/authorization/idempotency/worker tests |
| Reliability tests on disposable databases, RabbitMQ and EventStoreDB | 49 pass, 0 skipped; includes authoritative quotes, catalog/acceptance races, upgrades, durable saga decisions, crashes and compensation |
| Hungarian randomized verification | 300 seeded sparse rectangular matrices agree with exhaustive enumeration; dual feasibility and objective equality checked |
| EF model / migration consistency | No pending changes for WorkforceContext or FulfillmentContext |
| Locked NuGet restore | Pass |
| API and Gateway Docker builds | Pass; both restore locked dependencies and publish without another restore |
| API container planning smoke check | Pass: liveness and Swagger 200, complete two-slot USD 60 plan, objective = dual objective = 60 |
| Full API-container catalog/quote/workflow walkthrough | Pass: readiness 200, persisted catalog/skills, two USD 100 authoritative quotes, success then cancellation, capacity rejection then compensation; 14 PostgreSQL outbox events delivered, 0 dead letters |
| Git whitespace validation | Pass |
| Previous-increment independent GitHub Actions verification | Pass for workflow commit `54ca08b`: fast suites on Windows and Ubuntu, Linux reliability suite and API Docker build ([run](https://github.com/Ingaleee/FusionOps/actions/runs/38313824155)); catalog increment tracked separately |
| Independent Gateway container build | Workflow PR run passed ([run](https://github.com/Ingaleee/FusionOps/actions/runs/38313824148)); Gateway has no code change in the catalog increment |

The original ten reliability scenarios are:

1. Concurrent reservations of the same empty calendar produce exactly one winner and one durable event.
2. An injected commit failure rolls back state and outbox without clearing the original event.
3. Cross-database writes are rejected before either store is saved.
4. A poison event does not block valid messages and reaches dead-letter state on SQL Server.
5. The same poison-event scenario works on PostgreSQL.
6. Concurrent PostgreSQL dispatchers do not deliver the same locked row.
7. Retrying an audit append stores one event in EventStoreDB.
8. Allocation history is projected with a replay-safe checkpoint.
9. Actual RabbitMQ delivery preserves the concrete event contract and original MessageId.
10. Competing license-pool updates cannot oversell capacity or leave an event from the rejected write.

The idempotency increment adds seven acceptance scenarios: simultaneous identical keys execute once, fresh-scope canonical replay and conflict detection, rollback followed by retry with the same key, actor scope separation, independent-key concurrency, full rollback when a later resource in a batch conflicts, and the real HTTP status/body/Location/problem contract. The HTTP test follows Location to the created booking. The batch replay test reloads every persisted period and resource ID from a new context.

Earlier runs exposed a Docker Desktop IPC failure, then an owned-value tracking defect when multiple allocations shared a period. The final run follows recovery of the local engine and the complex-value mapping fix; all 17 scenarios pass on freshly migrated disposable stores. Windows used the current context's explicit Linux-engine endpoint. Period mapping changes EF tracking metadata while preserving existing physical columns and data.

The approved-workflow increment adds 19 database scenarios, bringing the active suite to 36:

1. Successful reservation spans both stores and commits a continuous versioned transition history.
2. Insufficient license capacity compensates the entire workforce batch.
3. Crash after resource commit replays original IDs and fences the old worker.
4. Crash after license commit replays the effect inbox without allocating again.
5. Cancellation during the resource step compensates its committed result.
6. Cancellation during the license step releases both stores.
7. Compensation/replay preserves another workflow's and ordinary project license ownership.
8. A lost resource reply pauses for operator resume and resolves the same effect.
9. A lost license-compensation reply resumes without duplicate release events.
10. A lost workforce-compensation reply resumes without duplicate cancellation events.
11. Concurrent claims skip a held coordinator lock and lease the remaining row once.
12. Deadline expiry compensates without discarding the original resource receipt.
13. Real HTTP proposal replay, ETag approval, stale mutation rejection and read/history contract.
14. A delayed resource worker cannot reverse a durable rejection after a calendar is freed.
15. A delayed license worker cannot allocate after compensation when capacity increases.
16. Rejection by a later license pool discards all earlier staged changes and events.
17. Concurrent proposal replay and approvals preserve one specification and one approved version.
18. The actual hosted worker advances approved HTTP proposals and cancellation without manual runner calls.
19. Upgrading earlier successful inbox records preserves their successful decision.

The authoritative-planning increment adds 13 database scenarios, bringing the active suite to 49:

1. Persisted skills/rates and exact fractional-hour decimal cost survive quote calculation and workflow execution in both databases.
2. HTTP allocation rejects unknown resource IDs without state or receipts.
3. A metadata change rejects a whole multi-resource quote and retains the negative decision even after restoring the old rate.
4. Concurrent workflows consume one quote once; successful replay survives expiry and compensation without resurrection.
5. Deactivation blocks new bookings and permits compensation of the original allocations.
6. A booking after calculation rejects acceptance without consuming the quote.
7. Expired quote rejection rolls back all staged allocations.
8. Real HTTP catalog preconditions and quote/workflow requests cannot replace stored economics, project or resources.
9. Concurrent catalog edit and acceptance either preserve the reviewed price or durably reject the changed revision.
10. Actual half-open calendars affect quote eligibility; incomplete quotes cannot start workflows.
11. Catalog upgrade rolls back legacy ID collisions, preserves historical bookings as inactive resources and installs an enforcing allocation foreign key.
12. Changed quote evidence is detected before effects and its rejection is durable.
13. Sub-microsecond quote boundaries are rejected rather than silently changing the cross-store period.

The latest full-container walkthrough uses the updated `examples/workflow-demo.ps1`, the built API image, fresh SQL Server/PostgreSQL/RabbitMQ/EventStoreDB and real background services. It registers four catalog resources/skill sets, calculates two complete USD 100 quotes, then verifies successful reservation, user cancellation and capacity compensation. Final states are Compensated/UserCancelled/version 7 and Compensated/Rejected/version 5. Readiness returned 200; PostgreSQL delivered 14 outbox events with no dead letters. The temporary verification stack was removed after checking persisted state and event delivery.

An initial catalog test exposed sub-microsecond period truncation in PostgreSQL. The final implementation rejects unsupported precision explicitly and stores quote timestamps at the common microsecond precision. The decimal quote amount uses PostgreSQL numeric without a fixed scale; exact fractional-hour cost is checked after reload. Final local total: 132 tests, zero failures and skips.

Test images: SQL Server 2022 CU14, PostgreSQL 16 Alpine, EventStoreDB 23.10.0, RabbitMQ 3 management Alpine. Testcontainers provides ephemeral isolation and random host ports. Recorded runs validate these versions; compatibility with other database versions is not implied.

Commands and generated TRX files are described in the README and CI workflow. The linked earlier GitHub runs independently verified the workflow increment; the catalog increment's CI result must be checked for its own commit. Docker images are local verification artifacts, not published releases. No throughput, tail-latency, availability or security certification is inferred from these tests.
