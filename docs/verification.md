# Verification record

Date: 2026-10-12. Local environment: Windows x64, .NET SDK 10.0.303 / runtime 10.0.12.

| Check | Result |
| --- | --- |
| Release solution build with warnings as errors | Pass, 0 warnings, 0 errors; 11 projects |
| Fast tests | 52 pass: 30 domain/algorithm/contract/architecture tests and 22 application/HTTP/authorization/idempotency tests |
| Reliability tests on disposable databases, RabbitMQ and EventStoreDB | 17 pass, 0 skipped; includes persisted idempotency, real HTTP replay/Location and multi-resource rollback |
| Hungarian randomized verification | 300 seeded sparse rectangular matrices agree with exhaustive enumeration; dual feasibility and objective equality checked |
| EF model / migration consistency | No pending changes for WorkforceContext or FulfillmentContext |
| Locked NuGet restore | Pass |
| API and Gateway Docker builds | Pass; both restore locked dependencies and publish without another restore |
| API container planning smoke check | Pass: liveness and Swagger 200, complete two-slot USD 60 plan, objective = dual objective = 60 |
| Git whitespace validation | Pass |
| Independent GitHub Actions verification | Pass for implementation commit `a59effa`: fast suites on Windows and Ubuntu, Linux reliability suite and API Docker build ([run](https://github.com/Ingaleee/FusionOps/actions/runs/38307258830)) |
| Independent Gateway container build | Pass on the PR ([run](https://github.com/Ingaleee/FusionOps/actions/runs/38307438200)) |

The ten reliability scenarios are:

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

Test images: SQL Server 2022 CU14, PostgreSQL 16 Alpine, EventStoreDB 23.10.0, RabbitMQ 3 management Alpine. Testcontainers provides ephemeral isolation and random host ports. Recorded runs validate these versions; compatibility with other database versions is not implied.

Commands and generated TRX files are described in the README and CI workflow. The linked GitHub runs independently verified the implementation after publication; subsequent documentation commits do not change the tested implementation. Docker images are local verification artifacts, not published releases. No throughput, tail-latency, availability or security certification is inferred from these tests.
