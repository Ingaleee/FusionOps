# FusionOps: next implementation decisions

Source review: 2026-10-13, implementation commit `e035daa`. This is a delivery proposal, not a record of implemented guarantees. The accepted [full objective](flagship-vision.md) and [milestone ledger](flagship-delivery.md) remain authoritative for scope and status.

Subsequent G3 increments connect [reviewed execution baselines](execution-plan-authority.md), [authoritative interval participants](temporal-participants.md), [fenced activation](execution-activation.md) and [operator-controlled task facts](execution-tasks.md), with the supported lifecycle and both local participant-commit process kills verified. The table and sequence below record the original review. G4 inventory/procurement is now the next delivery stage; adaptive supersession, financial closure and measured operations remain G5–G7. [Product strategy](product-strategy.md) and [logistics feasibility research](logistics-feasibility.md) preserve commercial options without replacing the Core programme.

## What the current code proves, and what it does not

The implemented foundation includes versioned project definitions and permissions, independently validated temporal scenarios, immutable decision evidence, authoritative catalog quotes, tenant isolation, durable reservation participants and fenced recovery. The last recorded verification is 191 passing tests, including real database/broker boundaries and an independent oracle for 100 small scheduling instances. Those checks establish particular invariants, not production capacity or completion of the project lifecycle.

The largest gap is between a temporal decision and an operational commitment:

| Source | Current contract | Consequence for the next stage |
| --- | --- | --- |
| `Domain/Scheduling/TemporalPlanner.cs` | Bounded grid search with task intervals, resource bundles and cost/finish objective | Keep this as a verified reference strategy; measure larger-instance quality and runtime before choosing a replacement |
| `Application/Scheduling/ITemporalScenarioStore.cs` | Create, inspect, compare and replay a scenario | A scenario currently cannot become an approved execution baseline |
| `Domain/Workflows/WorkflowContracts.cs` | One `TimeRange` and one resource set per reservation specification | Do not collapse a task schedule into one project-wide reservation; introduce interval-specific plan effects |
| `Infrastructure/Scheduling/TemporalSnapshotCapture.cs` | Separate observed SQL/PostgreSQL snapshots | Acceptance must reacquire authority against current state; a manifest does not reserve capacity |
| `Domain/Entities/LicensePool.cs` | Current occupied-seat count and durable reservation identity | Model future seat intervals explicitly before claiming temporal license ownership |
| `Domain/Entities/StockItem.cs` | Mutable quantity with deduct/restock operations | Introduce reservations and immutable movements before relying on inventory provenance or procurement recovery |
| `Gateway/Program.cs` | One global request-limiter partition | This does not establish per-tenant CPU admission or fair scheduling of long planning jobs |
| `.github/workflows/security-perf.yml` | Disabled job with a historical .NET 9 setup | There is currently no measured capacity gate in this workflow; create a reproducible experiment rather than advertise latency figures |

Paths in this table are relative to the repository and omit their `FusionOps.` prefix for readability. Existing workflow effect receipts remain useful; a general supplier/event consumer inbox is a separate contract.

## First priority: G3, approved temporal execution

Deliver a complete vertical path for projects whose demands are supported by the execution participants: calculate, review, approve, acquire reservations, run dependency-ordered tasks, recover a crash and inspect the outcome. Material/procurement requirements must remain explicitly ineligible for activation until G4 implements their participants. Hypothetical capacity must be resolved against actual authoritative capacity at admission; its presence in a scenario cannot authorize an effect.

Implement this in the following dependency order:

1. **Immutable execution baseline.** Bind tenant/project, definition version/digest, scenario identity/digest, validated task schedule, source observations, rule version and expected effects. Persist an explicit diff against any previous baseline. Reject partial, invalid or unsupported scenarios.
2. **Versioned approval.** Approval binds the exact baseline and policy version. Check project authority, separation of duties where required, expiry and optimistic concurrency. A changed baseline requires a new decision. Record which approval rule applied and why.
3. **Authoritative interval acceptance.** Acquire workforce/equipment reservations for the actual task intervals and license seats against an interval calendar. Each owner validates current capacity and relevant prices/revisions. Preserve one transaction per owning store. If sources have changed incompatibly, reject or request a new scenario rather than silently substitute different terms.
4. **Durable activation coordinator.** Stable effect identities include baseline/task/participant identity. Record durable participant decisions, fenced claims, retries and compensation ownership. A baseline becomes active only after all required participants confirm. Represent uncertain outcomes explicitly and resolve them before declaring success or completed compensation.
5. **Task execution lifecycle.** Separate planned intervals, owned reservations and actual execution facts. A task starts only under the active baseline, with completed dependencies and valid required ownership. Running, completed, blocked and unresolved states are durable; request retries cannot create a second start/completion effect.
6. **Executable demonstration.** Use a real API and both databases to approve a two-stage engineer/equipment/license plan. Introduce a competing reservation and kill a worker after a participant commit. Recovery returns the original effect without duplicate capacity consumption.

G3 exit gates:

- Editing a reviewed baseline invalidates its previous approval; stale approval/activation requests fail with no unauthorized task start.
- Concurrent acceptance of competing plans cannot overbook any shared interval. There is at most one active baseline per project; supersession is a later controlled transition, not a second activation shortcut.
- A crash after every participant commit can resume its original decision. A stale worker cannot record progress under an expired claim.
- A partial activation cannot start tasks, and compensation releases only effects owned by that activation. Unknown outcomes remain visible until reconciled.
- Adjacent half-open license reservations reuse capacity correctly; overlapping reservations obey the seat limit.
- The API walkthrough exposes the baseline, reviewed diff, approvals, reservations, task transitions and unresolved reasons.

## Remaining dependent increments

| Priority / milestone | Implementation | Acceptance evidence |
| --- | --- | --- |
| G4: Inventory and procurement | Warehouse/SKU ownership, stock reservations, immutable receipts/issues/transfers, purchase-order state machine, provider simulator, typed consumer inbox, discrepancy queue | Quantity conservation; duplicates/reordered notifications; supplier confirmation followed by lost response; reconciliation without a second shipment |
| G5a: Actual facts and adaptive planning | Facts carry source identity/version, effective time and recorded time; freeze completed work; retain irreversible commitments; calculate the unexecuted suffix; review the date/cost/resource diff before supersession | A supplier delay and machine failure preserve completed tasks; a new baseline cannot cancel an already shipped item; races between completion and replan are resolved explicitly |
| G5b: Project cost authority | Separate reserved, committed and actual amounts; immutable balanced postings within each currency; effect-linked original entries, reversals and corrections; operational reconciliation | No second posting on retry; balanced entries; controlled correction; over-budget commitment rejection; closure reconciles costs with operations |
| G6: Tenant operation | Provisioning, versioned approval policies, queued planning jobs, per-tenant work/concurrency budgets, fair admission and scheduling, cancellation/backpressure | A tenant flooding expensive planning cannot consume every execution/recovery slot; queue bounds, rejection behavior and the effect on another tenant are measured |
| G7: Operator and recovery proof | Operator views for baseline changes, active tasks, ownership and unknown outcomes; audited preconditioned recovery commands; scripted faults, load report, backup/restore and projection rebuild | Stale operator commands fail; recovery preserves original effect identity; reports include environment/workload/p95/p99/error rate/recovery time; restored state is reconciled across stores and providers |

Build a thin operator view alongside the API as each operational contract lands. A broad dashboard should consume stable domain decisions and evidence. It cannot supply missing ownership or reconciliation rules.

## Strengthen planning after execution contracts exist

The current deterministic what-if planner is the reference implementation. Improve it with constraint propagation, tighter resource/time bounds, better variable ordering and incumbent construction, while keeping the independent validator and small-instance oracle. Compare strategies on fixed sparse/dense graphs, scarce specialists, calendar fragmentation and competing projects. Publish complete-feasible rate, runtime, explored work and cost bounds; distinguish grid-model proof from a bounded feasible incumbent.

Extend the model incrementally with setup/maintenance, recurring calendars, allowed task splits, procurement lead times and the cost of changing an approved plan. These constraints must also exist in execution or have an explicit conservative acceptance rule. Define a versioned objective that explains the tradeoff between cost, deadlines and disruption instead of hiding it in an arbitrary weighted score.

Add risk simulation after supplier/actual-fact contracts are available: reproducible sampled supplier delays, equipment failures and repair durations, yielding distributions of finish dates and project cost. Retain seeds and assumption versions. A sampled model is scenario evidence; claiming predictive accuracy requires calibration against observations. Report uncertainty separately from deterministic feasibility.

## Architecture and proof rules

- Keep cohesive C# modules for project commitments, planning, plan authority, execution, inventory/procurement, cost and tenant operation. Each module owns its mutations and has explicit transactional boundaries. Separate deployments only when measurements or an ownership boundary justify them.
- Preserve at-least-once delivery semantics. Prevent repeated business effects with durable identities/receipts and provider reconciliation; do not advertise universal exactly-once external execution.
- Apply compensation to reversible ownership. Completed work, consumed materials and confirmed external shipments require recorded forward recovery or correction.
- CPU search must run outside database transactions. Durable jobs need bounded admission and cancellation; input limits alone do not provide fairness.
- Restore authoritative stores from supported backups, rebuild derived projections from retained evidence, and reconcile outstanding cross-store/provider operations. An audit stream alone is not proof that all command state can be rebuilt.
- Review migrations and protocol/schema changes against historical receipts and pending work. Restarting on a new version must not reinterpret an old successful effect as a new request.
- Keep advertised capabilities aligned with active code and executable examples. Historical/disabled infrastructure and placeholder modules should be repaired, clearly marked or removed as their role is resolved.

## Full completion demonstration

From a clean environment: create two tenants; model a multi-site project; compare alternatives; approve and activate an exact baseline; execute initial tasks; delay a supplier; make equipment unavailable; crash the worker immediately after a committed effect; reconcile duplicated/reordered observations; calculate and approve a remaining-work plan; resume execution; close with balanced cost and quantity evidence; restore into a fresh environment and verify ownership, original effects and unresolved operations.

The second tenant continues useful work during the incident. The report records the workload, environment and observed isolation/recovery behavior. This demonstration, together with invariant and protocol tests, is the completion target; adding more technologies or test cases alone is not.
