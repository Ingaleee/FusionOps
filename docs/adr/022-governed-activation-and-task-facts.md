# ADR 022: Governed activation, fenced coordination and task facts

Status: accepted, 2026-10-13. Extends ADRs 020/021 for the supported G3 workforce/equipment/license path; G4/G5/G6/G7 and the extended programme remain separate delivery gates.

## Context

Per-store interval decisions need cross-store orchestration and explicit project ownership. Approval does not itself grant resource ownership. A process can die after a participant commits but before the coordinator records that result. Cancellation/revocation must retain and release committed ownership, while started or completed work cannot be compensated as an unused reservation.

## Decision

Use one activation identity per immutable baseline, binding original actor/digest/approved version. Project-first admission locks verify current role, definition, approver and approval expiry. A tenant/project partial unique index retains one live activation throughout reservation, compensation and unknown results. Unsupported materials are rejected; late activation cannot silently move task intervals.

Keep short database-clock token/epoch claims and sorted participant ownership. Complete progress only under the current unexpired claim and step. Retain original participant decisions; an unresolved timeout retries its original identity or pauses for an authorized resume. Cancellation during an unknown reserve resolves that step before reverse-order release. Recheck current authority on confirmed results; lost authority compensates owned effects. State, an immutable decision and typed outbox event share the coordinator transaction.

Create task state from the exact admitted baseline. Require active execution, current project authority/definition, completed dependencies, actual resource/license ownership and the half-open planned interval before accepting start. Record actual completion separately from planned time, retaining overruns. Bind accepted/rejected task decisions to stable command/actor/version/data identities and append-only facts. Replay returns the original fact; stale versions cannot create a second transition.

Once a task starts, refuse generic cancellation and definition replacement while live. Full reported completion requests ownership release and a Completed activation outcome. This is operator-reported work with capacity closure, not customer/financial closure or certified physical execution. Future adaptive revision/forward recovery must preserve those facts and any irreversible commitments.

## Verification and limits

Real-store tests exercise competing project activations, participant commit loss at both stores, stale fencing, negative decisions, cancellation and authority revocation during commits, unknown-result/operator recovery, task races/dependencies/actual ownership/overruns, signed HTTP and restricted history policies. A separate Development-only failure lab kills its labeled API process after an actual participant commit and restarts ordinary configuration; the verification record distinguishes observed runs from test simulation.

Task SQL ownership is an owning-store observation coordinated with project/activation and license locks. It is not an atomic distributed snapshot, provider command or hardware interlock. External effects need dedicated adapter/inbox/reconciliation contracts. Started-work disruption, adaptive supersession, cost closure, tenant compute fairness, measured production capacity and backup/restore remain later stages. Original schema/event/receipt compatibility is retained.
