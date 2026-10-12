# ADR 013: explainable assignment with an optimality certificate

Status: accepted. Date: 2026-10-12.

## Decision

Planning accepts an explicit snapshot: a common time horizon, demand slots and resource candidates. Each candidate can fill at most one slot. Edges are forbidden if kind, skill proficiency, equipment type, calendar or currency does not match. A rectangular Hungarian solver finds a minimum-cost assignment using decimal arithmetic and shortest augmenting paths.

For a matrix with `n` rows and `m >= n` columns, complexity is O(n²m) and auxiliary workspace is O(n + m), excluding the input matrix. `null` represents a forbidden edge, so feasibility never depends on a magic large cost.

The planner adds a private dummy candidate to every slot. Its penalty is `n × maximumEligibleCost + 1`. Every complete real assignment costs at most `n × maximumEligibleCost`, so filling one more real slot always dominates cost minimization. Among maximum-cardinality plans, the solver minimizes actual cost.

The result reports real monetary cost separately from the optimization objective (which includes dummy penalties). Row and column dual potentials provide an independently checkable lower bound. The response exposes primal and dual objective values; solver tests verify dual feasibility, tight matched edges, and equality of the objectives.

## Validation

Tests compare 300 seeded, sparse, rectangular matrices with exhaustive enumeration, including negative costs at the generic solver level. Resource planning itself requires nonnegative rates. A counterexample demonstrates row-wise greedy cost 101 versus optimal cost 4. A business example preserves the only specialist for the specialist slot, rather than consuming them for a general slot.

## Limits

This is assignment over one fixed horizon, not a general multi-period scheduling solver. It does not optimize overtime, travel, interchangeable inventories, task precedence or procurement lead times. Planning does not reserve resources: the write path rechecks availability under locks. There is no implicit FX conversion. The API bounds requests to 50 slots, 200 candidates, 20 skills per candidate/slot and 64 busy intervals per candidate, with a 1 MiB HTTP body limit. GUID ordering stabilizes equal-cost ties.

Versioned snapshots and an idempotent plan-application protocol are planned next.
