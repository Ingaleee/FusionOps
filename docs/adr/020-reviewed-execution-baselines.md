# ADR 020: separate immutable execution evidence from approval state

Status: accepted, 2026-10-13; approval implementation increment, activation pending.

Temporal scenarios prove feasibility under captured conditions without acquiring capacity. The existing reservation workflow owns one resource interval and cannot represent a project schedule by stretching that interval across every task. Plan review must not silently imply successful participant acceptance.

Persist an append-only `ExecutionBaseline` containing the original scenario proof, definition identity, versioned policy and change diff. Store the mutable review state separately in `ExecutionPlan`, with append-only versioned decisions and a transactional outbox. Require the reviewed digest and current ETag for decisions. Pin the server policy at proposal time and recheck current project authority/definition under a parent-first lock.

This gives approval a stable meaning and preserves historical requests across source changes. It also creates explicit room for later participant receipts and an activation state machine. Multiple proposals are allowed; one active execution and supersession require their own authoritative protocol. Approval expiry is an activation precondition rather than permission to undo irreversible effects.

The retained proof duplicates scenario evidence to make the baseline self-inspectable, with a bounded size. Metadata pages avoid this payload. Digests are integrity evidence under the application's trust model, not protection from a database administrator rewriting both data and digest. PostgreSQL runtime policies and ORM guards forbid history edits; migration/backup administrators retain their necessary authority.

Alternative rejected: directly mutate scenario JSON into execution state or feed all temporal tasks into one existing reservation specification. Either would obscure the reviewed version or acquire materially different intervals from the schedule.

See [execution contract](../execution-plan-authority.md), [temporal scenarios](../temporal-planning.md) and [extended programme](../ultimate-roadmap.md).
