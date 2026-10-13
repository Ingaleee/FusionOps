# ADR 021: Task-interval capacity and original participant decisions

Status: accepted, 2026-10-13. Scope: internal G3 participants; activation coordination and task execution remain incomplete.

## Context

A reviewed scenario assigns different intervals to individual tasks. The existing reservation workflow owns one shared interval, and permanent license allocations have no end time. Reusing that workflow unchanged would acquire the wrong calendar and miss seat conflicts between concurrent plans. Losing a committed result must not turn a retry into another reservation or a different decision.

## Decision

Implement workforce/equipment acceptance as one serializable SQL Server decision per baseline: sorted resource/catalog locks, actual task intervals, original allocation mappings, and state/events/command receipt in one transaction. A conflict commits a negative receipt after discarding staged effects. Release verifies the original owned mappings and records a separate result.

Implement named license pools with task/plan/project interval ownership in PostgreSQL. End-before-start sweep semantics allow adjacent bookings. Permanent allocations consume capacity across every future interval; their admission subtracts outstanding temporal peak demand. Lock the plan effect identity and pools in deterministic order, validate the observed pool identity and real current capacity, and commit all pools/events/an append-only positive or negative receipt together. A rejection cannot leave a partial multi-pool reservation. Release verifies exact ownership; old reserve replay after release never recreates bookings.

Bind license bookings and effect receipts to their exact tenant/project/baseline with composite foreign keys and row policies. Keep mutable capacity ownership separate from original immutable decision evidence. The threat boundary still trusts database administrators and the internal worker ports; no public endpoint may bypass approval/activation admission through these participants.

Capture existing license calendars in temporal scenarios. Omit the nullable calendar field from old canonical input, retain the v1 solver identity for null-calendar history, and use v2 for explicit calendars. A proof fixture captured from published d820e84 checks original bytes, digest and replay. Schema changes must preserve existing pending decisions, not silently recalculate their effects.

## Consequences and proof limits

Cross-store atomicity is supplied by future durable coordination, not by either participant transaction. A baseline becomes active only after required participants confirm under current project/approval authority; unsupported materials stay ineligible until G4. Unknown results need original-decision reconciliation, and compensation must not race an unfinished reserve attempt.

Domain oracle tests, real-store concurrent/negative/replay/release tests, actual PostgreSQL policies and source replay cover the implemented boundaries. Discarding a returned commit result models retry recovery; a complete killed-worker demonstration requires the activation coordinator. No task execution, project supersession, procurement or financial closure is implied by this ADR.
