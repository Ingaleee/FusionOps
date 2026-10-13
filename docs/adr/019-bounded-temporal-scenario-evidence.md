# ADR 019: bounded temporal search and retained scenario evidence

Status: accepted, 2026-10-13.

## Decision

Use a deterministic bounded grid search as the first authoritative multi-period model. Enumerate absolute starts and distinct resource bundles in a stable topological task order; scheduling a task later does not prevent a later-enumerated independent task from starting earlier. Complete schedules are ordered by modeled cost and project finish. Bound trials/states through node/time budgets and request cancellation. Return feasible incumbents/partial prefixes with explicit exhaustion status; only exhaustive completion supports a grid-model optimality claim.

An independent validator recomputes feasibility and cost from proposed intervals/assignments. It does not share search state, pruning or cached capacity calculations. Fixed lower bounds ignore resource exclusivity/calendar competition. They describe a relaxation, not a promise that its cost can be achieved. Small seeded fixtures compare the solver to independent Cartesian enumeration.

Capture registered project/membership/stock/license data in one PostgreSQL repeatable-read observation, and catalog/calendars in one serializable SQL observation with sorted catalog locks and explicit tenant/resource seeks. Preserve source versions, observation times and complete observed inputs. Release source transactions before CPU search. These are separate store observations; future acceptance must recheck current participant state.

Separate basis evidence from hypothetical unavailability/capacity assumptions and applied input. No simulation updates resource calendars, stock or licenses. Persist immutable scenario evidence linked by composite foreign key to the exact tenant-owned definition revision. Scenario ID resolves one original actor/request; state/evidence and its audit/outbox event commit together. Before append, lock the project and recheck current planning membership, version and definition digest.

Replay uses retained input and reports whether the recomputed decision matches. It neither rereads commercial sources nor creates a new operational effect/event. Wall-clock-limited search can differ across hosts/runs; deterministic node limits/exhaustive search reproduce decisions when time does not interrupt them. Comparison exposes version/hypothetical status and changed task/cost/finish evidence.

## Tradeoffs and next boundary

Exponential enumeration is deliberately bounded and suitable for measured workloads, not an unbounded production optimizer claim. Current model uses explicit UTC site windows and nonpreemptive tasks, current stock quantity, and conservatively unavailable already allocated license seats. Cost includes workforce/equipment rates and material unit prices. Future solver strategies, overtime, supplier lead times, recurring calendars, task splitting and disruption policy require explicit models and evidence.

The next G3 boundary is versioned review/approval of a complete nonhypothetical baseline, then participant acceptance and a durable task execution lifecycle. A scenario's digest or SearchComplete flag alone does not acquire capacity or authorization to execute it.
