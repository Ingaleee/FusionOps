# Verification record

Updated: 2026-10-13. Local environment: Windows x64, .NET SDK 10.0.303 / runtime 10.0.12.

| Check | Result |
| --- | --- |
| Release solution build with warnings as errors | Pass, 0 warnings, 0 errors; 11 projects |
| Fast tests | 76 pass: 43 domain/algorithm/contract/architecture/workflow tests and 33 application/HTTP/authorization/idempotency/worker tests |
| Reliability tests on disposable databases, RabbitMQ and EventStoreDB | 36 pass, 0 skipped; includes durable saga decisions, crashes, compensation, hosted worker and migration upgrade |
| Hungarian randomized verification | 300 seeded sparse rectangular matrices agree with exhaustive enumeration; dual feasibility and objective equality checked |
| EF model / migration consistency | No pending changes for WorkforceContext or FulfillmentContext |
| Locked NuGet restore | Pass |
| API and Gateway Docker builds | Pass; both restore locked dependencies and publish without another restore |
| API container planning smoke check | Pass: liveness and Swagger 200, complete two-slot USD 60 plan, objective = dual objective = 60 |
| Full API-container workflow walkthrough | Pass: readiness 200, success then cancellation, capacity rejection then compensation; 14 PostgreSQL outbox events delivered, 0 dead letters |
| Git whitespace validation | Pass |
| Previous-increment independent GitHub Actions verification | Pass for implementation commit `a59effa`: fast suites on Windows and Ubuntu, Linux reliability suite and API Docker build ([run](https://github.com/Ingaleee/FusionOps/actions/runs/38307258830)); workflow increment tracked separately |
| Independent Gateway container build | Prior PR run passed ([run](https://github.com/Ingaleee/FusionOps/actions/runs/38307438200)); Gateway has no code change in the workflow increment |

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

The full-container walkthrough uses the shipped `examples/workflow-demo.ps1`, the built API image, fresh SQL Server/PostgreSQL/RabbitMQ/EventStoreDB and real background services. It verifies successful reservation, user cancellation and capacity compensation; recorded final states are Compensated/UserCancelled/version 7 and Compensated/Rejected/version 5. Readiness returned 200, and the planning example still returned its complete USD 60 plan. The temporary verification stack was removed after checking persisted state and event delivery.

Test images: SQL Server 2022 CU14, PostgreSQL 16 Alpine, EventStoreDB 23.10.0, RabbitMQ 3 management Alpine. Testcontainers provides ephemeral isolation and random host ports. Recorded runs validate these versions; compatibility with other database versions is not implied.

Commands and generated TRX files are described in the README and CI workflow. The linked earlier GitHub runs independently verified the previous increment; the workflow increment's CI result must be checked for its own commit. Docker images are local verification artifacts, not published releases. No throughput, tail-latency, availability or security certification is inferred from these tests.
