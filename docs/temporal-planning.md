# Temporal project scenarios

G2 calculates persisted, independently validated schedules from a registered project's current definition and observed catalog/calendars/stock/licenses. Calculation, comparison and replay do not reserve capacity or execute tasks. G3 now retains a [reviewed baseline](execution-plan-authority.md) and has internal [temporal participants](temporal-participants.md); activation orchestration and task execution remain unfinished.

## Model and objective

Algorithms `temporal/bounded-grid-cost-finish-v1` (historical null license calendar) and `temporal/bounded-grid-cost-finish-v2` (explicit existing license bookings) enumerate task starts and distinct resource bundles on an explicit grid anchored at the project horizon start. Tasks are nonpreemptive and finish-to-start dependencies must be met. A resource bundle occupies all selected people/equipment for the entire task interval. Site open windows, task deadlines, milestones, existing bookings, skills/types, total material availability, simultaneous license capacity and the project budget are hard constraints.

The objective first requires a complete feasible schedule, then minimizes modeled cost, then project completion time. Stable task/candidate/slot ordering makes completed/node-limited search reproducible. An exhaustive result proves this objective only for the captured candidate set, explicit grid, supplied model and snapshot. It does not prove a continuous-time or fleet-wide optimum. A finer grid can produce different opportunities and takes more work.

Modeled cost includes catalog hourly rates times exact task minutes plus materials at captured unit cost. Existing license contracts have no authoritative price; there is no invented license price. Tax, procurement/transport fees, overtime, equipment setup/maintenance except observed unavailable intervals, recurring calendars, task splitting and approved-plan disruption objectives are later contracts. The stored model makes these boundaries inspectable.

Materials are consumed once per task and require enough total observed stock. Current permanent license allocations are conservatively unavailable for the entire horizon; captured temporal reservations occupy only their actual intervals. Future releases are not guessed. A hypothetical capacity addition must name an observed required product/SKU; material additions require a known unit price. Hypothetical availability and capacity are explicit assumptions and cannot silently become a real commitment.

## Result interpretation

| Field | Meaning |
| --- | --- |
| IsComplete | All tasks appear in a schedule that passed the independent validator |
| SearchComplete | The modeled search was exhausted or a necessary-condition shortage was established; no node/time cutoff |
| Termination | exhausted, node_budget_exhausted or time_budget_exhausted |
| ExploredNodes | Deterministic budget units charged for search states, start trials and resource option trials |
| CostLowerBound | Relaxation that ignores resource exclusivity/calendar competition and uses each slot's cheapest eligible candidate, plus material costs |
| TotalCost | Recomputed modeled cost of returned tasks; for a partial prefix this is not a cost to finish the whole project |
| Issues | Static shortages/conflicts, search cutoff and explicitly unscheduled tasks |

A cutoff with no complete incumbent does not prove infeasibility. A complete incumbent after cutoff is feasible but has no optimality claim. A complete exhaustive schedule has a grid-model optimum; a completed search with no complete schedule proves absence within that model. Partial output is a valid dependency-closed prefix and cannot be approved as a full plan. The independent validator checks duration/grid, dependencies, windows/milestones, slot coverage, resource eligibility/collisions, material/license quantities and recomputed cost without search caches/pruning.

GridMinutes is 1–60. MaxSearchNodes is 1–1,000,000 and MaxElapsedMilliseconds is 1–30,000 (defaults 15, 50,000 and 5,000). Time budgeting applies to the search; capture/validation are separately bounded by input limits and request cancellation. Cancellation is checked during snapshot operations and search and does not persist a scenario. Wall-clock cutoff can change an incumbent between replay runs; node-limited/exhaustive decisions are reproducible when time does not interrupt them. Replay reports whether the decision actually matches, rather than claiming equality unconditionally.

## API

| Route | Behavior |
| --- | --- |
| POST /api/v1/projects/{projectId}/scenarios | id, expectedProjectVersion, candidateIds, gridMinutes, maxSearchNodes, maxElapsedMilliseconds and optional assumptions; 201/Location/digest ETag |
| GET /api/v1/projects/{projectId}/scenarios?after={id}&limit=20 | Metadata page, 1–100 scenarios |
| GET /api/v1/projects/{projectId}/scenarios/{id} | Original basis, source manifest, assumptions, applied input, result and validator evidence |
| POST /api/v1/projects/{projectId}/scenarios/{id}/replay | Recalculate original input without rereading commercial sources or creating effects; return validation/match |
| GET /api/v1/projects/{projectId}/scenarios/compare?left={id}&right={id} | Definition versions, hypothetical flags, cost/finish deltas and changed task IDs |

Read/compare require project membership. Calculation/replay require project planning authority and Resource.Manager outside Development. All modes require a registered project. CandidateIds is an explicit set of up to 200 unique IDs; it may be empty when tasks need no workforce. Rates, stock quantities, source bookings and project definitions are not supplied by the caller.

Scenario IDs resolve their original actor/request/evidence. Input-order-equivalent retry returns the original snapshot even after source or project changes, provided the caller still has planning access. A changed request under the same ID returns 409. A new request referencing a stale project aggregate version returns 412. Revoked membership cannot read or replay previous evidence.

## Sources and durability

PostgreSQL provides a repeatable-read project/membership/stock/license observation. SQL Server locks catalog/calendars in sorted resource order and uses explicit tenant/resource seeks under serializable isolation. These observations are separate transactions. The manifest contains project/definition versions and digest, observation times, catalog revisions, stock quantities/prices/IDs and license pool versions/totals/allocations. Exact observed calendars are in the basis input. This is evidence of what each store contributed, not a cross-store atomic snapshot or a capacity reservation.

Release both snapshot transactions before CPU search. Before append, lock the project again and recheck current planning authority, aggregate version and definition digest. A concurrent project change cannot save a new stale scenario. A participant may change after its observation; future acceptance must recheck those conditions. History remains useful evidence of the original decision.

The additive PostgreSQL TemporalScenarios migration links each scenario to its exact tenant-owned immutable definition revision. SELECT/INSERT policies and the EF write guard forbid runtime update/delete of scenario evidence. State/evidence and the original TemporalScenarioCalculated outbox event commit together. A digest binds tenant, scenario/project IDs, versions, original actor/request and retained evidence. It detects changes but cannot defend against an administrator rewriting all evidence.

## Verification

The independent Cartesian oracle compares 100 seeded small scheduling instances on cost/completion time and infeasibility. Dedicated cases cover the specialist greedy trap, multi-resource bundles, dependencies, split shifts, existing bookings, simultaneous/adjacent licenses, material pricing/budget, invalid proposed schedules, cancellation and budget exhaustion.

Real-store scenarios cover persisted original replay after edits, hypothetical stock/availability alternatives, concurrent identical submissions, stale version and cross-store capture races, signed-JWT project permissions, bounded metadata pages, committed calendars and append-only evidence under a restricted database role. See [verification](verification.md), [project authority](project-commitments.md) and [delivery status](flagship-delivery.md).
