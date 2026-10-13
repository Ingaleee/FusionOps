# Verification record

Updated: 2026-10-13. Local environment: Windows x64, .NET SDK 10.0.303 / runtime 10.0.12.

| Check | Result |
| --- | --- |
| Release solution build with warnings as errors | Pass, 0 warnings, 0 errors; 11 projects |
| Fast tests | 140 pass: 98 domain/algorithm/contract/architecture/workflow/quote/project/temporal/execution-review/license-calendar/activation/task tests and 42 application/HTTP/authorization/idempotency/worker/tenant tests |
| Reliability tests on disposable databases, RabbitMQ and EventStoreDB | 111 pass, 0 skipped; includes activation/task lifecycle, actual time/ownership/dependencies/overruns, original decisions, competing admission, signed HTTP/restricted policies, temporal scenarios, project/tenant authority, quotes, durable saga decisions, crashes and compensation |
| Hungarian randomized verification | 300 seeded sparse rectangular matrices agree with exhaustive enumeration; dual feasibility and objective equality checked |
| EF model / migration consistency | No pending changes for WorkforceContext or FulfillmentContext |
| Locked NuGet restore | Pass |
| API and Gateway Docker builds | Pass; both restore locked dependencies and publish without another restore |
| API container planning smoke check | Pass: liveness and Swagger 200, complete two-slot USD 60 plan, objective = dual objective = 60 |
| Full API-container catalog/quote/workflow walkthrough | Pass: readiness 200, persisted catalog/skills, two USD 100 authoritative quotes, success then cancellation, capacity rejection then compensation; 14 PostgreSQL outbox events delivered, 0 dead letters |
| Full API-container tenant walkthrough | Pass on the final image after calendar-lock correction: readiness 200, independent USD 50/USD 198 quotes, scoped capacity under the same product name, denied foreign reads/reservations, both reservations compensated |
| Full API-container project walkthrough | Pass: readiness 200, original creation replay, distinct aggregate/definition versions, historical budgets retained, stale ETag/cycle/foreign tenant/owner removal rejected |
| Full API-container temporal walkthrough | Pass: readiness 200, USD 60/two-hour schedule, hypothetical machine delay adds one hour, historical replay after project revision, foreign/stale access rejected, no reservations |
| Full API-container execution-review walkthrough | Pass: readiness 200, original scenario proof/digest, ETag approval, immutable decisions, original proposal replay and +60-minute alternative awaiting fresh review; no capacity effects |
| Git whitespace validation | Pass |
| Independent GitHub Actions verification | Pass for catalog implementation commit `8181e3a`: fast suites on Windows and Ubuntu, Linux reliability suite and API Docker build ([run](https://github.com/Ingaleee/FusionOps/actions/runs/38318219468)) |
| Independent Gateway container build | Pass for the same implementation commit ([run](https://github.com/Ingaleee/FusionOps/actions/runs/38318219382)) |
| Tenant increment published CI | The same Windows/Ubuntu fast suites, Linux reliability suite and API/Gateway container checks run on the branch; current results are available in [PR checks](https://github.com/Ingaleee/FusionOps/pull/1/checks) |

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

The catalog increment's full-container walkthrough uses the updated `examples/workflow-demo.ps1`, the built API image, fresh SQL Server/PostgreSQL/RabbitMQ/EventStoreDB and real background services. It registers four catalog resources/skill sets, calculates two complete USD 100 quotes, then verifies successful reservation, user cancellation and capacity compensation. Final states are Compensated/UserCancelled/version 7 and Compensated/Rejected/version 5. Readiness returned 200; PostgreSQL delivered 14 outbox events with no dead letters. That temporary verification stack was removed after checking persisted state and event delivery.

An initial catalog test exposed sub-microsecond period truncation in PostgreSQL. The final implementation rejects unsupported precision explicitly and stores quote timestamps at the common microsecond precision. The decimal quote amount uses PostgreSQL numeric without a fixed scale; exact fractional-hour cost is checked after reload. The catalog increment completed with 132 local tests, zero failures and skips.

The tenant increment adds seven fast JWT/notification cases and ten database scenarios:

1. Foreign catalog/quote reads, edits, allocation and workflow proposal cannot consume another tenant's resources.
2. Identical product/SKU names have independent capacity and restock effects.
3. Concurrent identical actor/key requests in different tenants retain independent receipts and events; the race repeats eight times with fresh state.
4. Global worker claims execute both participants and compensation in the persisted workflow tenant.
5. Foreign attached writes fail; a context cannot change its scope after an untracked read; missing scope fails closed.
6. SQL raw/unfiltered reads and foreign writes remain scoped; composite foreign keys reject a foreign resource owner.
7. Restricted PostgreSQL principals cannot read foreign root/owned rows or enable global access; pooled checkout/reset preserves isolation.
8. Tenant audit streams project and replay into separate history rows.
9. Restricted SQL principals preserve scope through physical connection reuse and cannot enable system authority with a flag alone.
10. An EventStore append containing the exact pre-tenancy JSON is resolved by upgraded outbox delivery without creating a second event.

The final review reproduced a sparse-calendar deadlock: the SQL graph showed shared PK_Allocations ranges followed by competing inserts. A predicate alone did not stop the optimizer choosing a clustered scan. Locked reservation reads and quote calendar reads now explicitly seek the tenant/resource index; materialization uses the same query as lock acquisition. The repeated independent-tenant race checks this sparse-table case. Legacy audit retries preserve their old stream and omitted optional tenant field. The initial Windows verification also required the .NET named-pipe URI form documented in the README; a testhost IPC timeout was rerun with appropriate process permissions.

Test images: SQL Server 2022 CU14, PostgreSQL 16 Alpine, EventStoreDB 23.10.0, RabbitMQ 3 management Alpine. Testcontainers provides ephemeral isolation and random host ports. Recorded runs validate these versions; compatibility with other database versions is not implied.

Final local tenant total: 149 tests, zero failures or skips. Repeated NUnit concurrency attempts are additional executions of one reported test case. Both EF models match the migrations. SQL Server/PostgreSQL policies are exercised through actual restricted principals, not inferred from filtered repository results.

Both shipped PowerShell demonstrations also pass on the final API container. The new tenant run delivers nine PostgreSQL outbox events per tenant, independently of the legacy walkthrough's fourteen events. Four walkthrough pairs completed during verification; the final database inspection found all 128 PostgreSQL outbox messages delivered and zero dead letters across the legacy tenant and eight new tenants. The final check waits for delivery completion rather than assuming workflow completion implies an empty outbox.

G1 adds 17 domain cases and eight database/production-HTTP cases. 120 seeded DAGs agree with independent longest-path enumeration. Signed JWT scenarios prove tenant manager roles do not bypass project membership, read-only members cannot edit, Planner cannot approve, Approver can approve, and existing quote/allocation/license/workflow/audit endpoints enforce project permissions. Project create/edit races retain one accepted state, revision and event. A restricted PostgreSQL principal cannot change/delete definition history or create a foreign-owner child. Local G1 total: 174 tests, no failures or skips; the models match migrations. The shipped `examples/project-demo.ps1` also passes against the full API container.

Final G1 container checks also repeat the existing workflow and two-tenant walkthroughs. All 38 PostgreSQL events from two project demonstrations and the two earlier walkthroughs are delivered with zero dead letters. Project pages return metadata/actor roles rather than serializing every graph; the production HTTP fixture checks that contract.

G2 adds nine domain cases and eight database/HTTP cases. The exact Cartesian oracle checks 100 seeded small schedules independently on feasibility, cost and completion time. Other cases cover joint specialist/bundle choice, calendars/bookings, licenses at half-open boundaries, material costs, lower bound/cutoff interpretation, cancellation during search and tampered evidence. Database cases prove capture of real versions/calendars/prices, retained replay after source changes, explicit hypothetical alternatives, concurrent submission, no reservation effects, project change during cross-store capture, signed actor access and append-only evidence under a restricted PostgreSQL role. Local G2 total: 191 tests, no failures or skips. Both EF models match migrations; the API image and shipped temporal demonstration pass.

The G2 container also passes the project, workflow and two-tenant walkthroughs after applying the new migration. All 39 PostgreSQL outbox messages are delivered with zero dead letters, including two scenario events. The temporal replay retains its original decision after revising the project and does not create another event or reserve capacity.

Commands and generated TRX files are described in the README and CI workflow. The linked GitHub runs independently verified the catalog implementation commit; subsequent documentation changes do not change that code evidence. Docker images are local verification artifacts, not published releases. No throughput, tail-latency, availability or security certification is inferred from these tests.

The G3 review increment adds six domain cases and seven integration cases: detached/tampered baseline evidence; digest/policy/separate-approver/expiry/state rules; versioned task/currency diffs; retained original proposals; competing approval/rejection; definition changes and fresh proposals; partial-plan rejection; actual restricted-role PostgreSQL history policies; signed production HTTP contracts; and concurrent original proposal deduplication. The first integration run found a test helper attempting JSON parsing of an expected empty 404 response; the corrected test asserts the status. The final run passes all 82 reliability cases; combined local total is 204, no failures or skips. Both EF models match migrations.

The final `fusionops-api:execution-verification` image passes the new execution-review walkthrough (including the temporal demo), and the project, workflow and two-tenant walkthroughs. All 45 PostgreSQL outbox events are delivered with zero dead letters: three `v1.ExecutionPlanChanged`, four temporal scenario events and the existing project/workflow/license events. The temporary compose stack is scoped to `fusionops-execution-verify`; its RabbitMQ cookie ownership issue was corrected only in that stack before verification. Approval is not yet temporal execution: authoritative interval acceptance, task lifecycle and corresponding participant-crash recovery remain G3 work.

The temporal participant increment adds six domain cases and eleven integration cases. One hundred seeded seat calendars agree with a separate discrete peak oracle; adjacent intervals and ordinary/temporal capacity checks are exercised. A golden scenario proof captured from the published d820e84 Domain binary retains its original JSON, digest and deterministic replay despite the added optional license-calendar field. Explicit calendars use solver protocol v2 while null-calendar history retains v1.

Real SQL Server/PostgreSQL tests cover original success, permanent negative decisions, release replay, retained allocation IDs, multi-pool rollback, concurrent identical requests, different competing baselines, adjacent project reservations, source capture/replay and actual restricted-role booking/receipt policies. The initial integration run exposed a SQL participant adding outbox entries while enumerating the live EF entry collection. Taking the source snapshot before staging events corrected the defect; the final run passes all 93 reliability cases. Combined local total is **221**, zero failures or skips. Both models match migrations and the Release solution build has zero warnings/errors.

The final `fusionops-api:temporal-participant-verification` image applies `20261013110713_TemporalLicenseReservations`, becomes ready and passes the execution-review/temporal, project, workflow and tenant walkthroughs. All 45 PostgreSQL outbox events are delivered, zero dead letters. This API exercise proves migration/readiness and existing-contract compatibility; trusted internal participant effects are exercised directly against real databases in the integration suite. The temporary stack is scoped to `fusionops-temporal-participant-verify`; only its RabbitMQ cookie ownership was repaired.

At that participant-only increment, per-store commits were verified while the coordinator/task lifecycle remained unfinished. Discarding a committed participant reply proved original-result replay within that boundary, not a complete killed-worker experiment. [ADR 021](adr/021-temporal-capacity-participants.md) records those original limits; subsequent [ADR 022](adr/022-governed-activation-and-task-facts.md) and the evidence below supply the supported lifecycle. No performance claim follows from the test count.

The next G3 increment delivers activation and the supported operator-controlled task lifecycle. Eight domain cases cover reviewed admission, cancellation/unknown results, confirmed rejections, authority loss and resource-result validation; four task-domain cases cover current windows, actual completion/overruns, capacity closure and negative facts. Thirteen integration cases cover single-project activation races, both committed participant reply losses with lease replacement and stale fencing, license rejection/compensation, both authority-revocation boundaries, cancellation during license commit, unknown/operator recovery, actual history policies, signed HTTP and hosted worker. Five task integration cases exercise real one-minute intervals and incomplete dependencies, competing starts/original command replay, true late completion, current resource/seat ownership removal, signed HTTP and restricted append-only fact policies. The combined final total is **251**, zero failures/skips. Both models match migrations; the Release solution build has zero warnings/errors. New activation fixtures use future times derived from the current clock so admission tests do not expire on a fixed calendar date.

The final API image applies both `20261013114242_GovernedExecutionActivation` and `20261013135418_RecordedExecutionTasks`. `examples/execution-task-demo.ps1` passes the full review/activation/task/ownership-close path using actual time, including dependency denial, immutable start/completion replay and retained overrun. The first walkthrough exposed PowerShell automatically materializing response timestamps as DateTime; casting those values directly fixed the script's locale-dependent Parse call. The final walkthrough and the existing project, workflow and tenant walkthroughs all pass.

Actual failure experiments use `scripts/verify-execution-recovery.ps1` and the isolated Development compose fixture. At each selected participant commit, the local commit probe pauses before coordinator completion; the script verifies its project label and sends SIGKILL only to that API container. After a normal-config restart and lease replacement, original decisions produce Active ownership, then confirmed cancellation releases it. SQL-resource boundary: ReservingResources/version 1/epoch 1 before kill, Compensated/version 6/epoch 5 after recovery. PostgreSQL-license boundary: ReservingLicenses/version 2/epoch 2 before kill, Compensated/version 6/epoch 5 afterward. Both retain three original task/resource allocation mappings; original activation replay does not restart compensation. The [recorded local evidence](evidence/execution-recovery-2026-10-13.json) includes boundary state, epochs, mappings and API image identity. These are actual local process kills, not region/database/provider failure or financial recovery evidence.

All **145 PostgreSQL outbox events** in the verification stack are delivered, zero dead letters, including 33 activation decisions, six task command decisions and eleven temporal license changes. The stack also contains the earlier failed walkthrough's retained partial fixture, so this total is a transport-delivery observation rather than a claim that every prototype project completed. It is removed as a scoped temporary environment after verification. G3 is delivered for the stated supported scope; material/procurement, external physical effects, adaptive supersession, financial closure, fairness, load measurements and exercised restore remain G4–G7.

Reproduce local commit-kill verification with `pwsh -File scripts/verify-execution-recovery.ps1 -Boundary ReservingResources`, then `-Boundary ReservingLicenses`. The script builds a local API image by default, uses loopback ephemeral ports, checks compose ownership before killing the API, preserves its binding through replacement, waits for recovery and removes only its temporary stack unless `-KeepStack` is explicitly supplied. The commit probe is unavailable through HTTP and production refuses failure-lab configuration. The reports do not infer throughput, p95/p99 or production recovery objectives.
