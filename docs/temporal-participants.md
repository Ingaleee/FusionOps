# Authoritative temporal reservation participants

G3 implementation increment. These internal participants implement atomic decisions in each owning store for a reviewed baseline. The [activation coordinator](execution-activation.md) enforces approval, current project authority/definition, supported demands and one live baseline before using these trusted worker ports. No public route directly calls a participant. Approval alone does not reserve capacity or start a task; [task commands](execution-tasks.md) have a separate admission contract.

## Workforce and equipment

`SqlTemporalWorkforceParticipant` reserves every selected resource for each task's actual interval, in one SQL Server transaction. It does not replace the schedule with one horizon-wide allocation. It locks distinct resources in sorted order, rechecks current active catalog revisions, uses the existing tenant/resource calendar index and range-lock protocol, and checks new intervals against earlier intervals in the same batch.

Success retains task/resource/allocation identity mappings in a tenant/worker/operation/plan-scoped durable command receipt. A conflict or changed catalog discards all staged allocations/events but retains the transaction locks and commits an original rejection. State, original allocation events and the receipt share a commit. If the result is lost after commit, retry returns the original mappings without a second allocation. Current source changes cannot convert a recorded failure into a later success under the same effect identity.

Release looks up that exact original receipt and verifies each allocation's resource/project/interval. It cancels only those owned identities and persists a distinct release receipt. Other allocations survive. A delayed reserve replay after release returns its earlier result without recreating cancelled ownership. Missing or changed original ownership requires investigation; a timeout does not imply absence of an effect. SQL receipts have the existing trusted-server/database-owner threat boundary.

## License capacity over time

`LicensePool` now has temporal reservations keyed by plan/task under the tenant/pool owner. Reservations occupy half-open intervals. Adjacent intervals reuse a seat; overlapping demand is checked with end events before start events at an equal timestamp. Ordinary/workflow allocations without an end time consume capacity throughout the future calendar. Both permanent and temporal admission paths account for the same total pool: permanent admission conservatively subtracts the peak of all outstanding temporal reservations.

`PostgresTemporalLicenseParticipant` locks a plan effect identity and then named pools in sorted order. It checks the observed pool identity and actual current capacity for all task intervals. Compatible capacity/version changes need not reject a plan with unchanged license terms; an incompatible shortage records a negative decision. Unknown capacity and hypothetical additions do not create seats.

All pool changes, typed events and one append-only original success/rejection receipt share a PostgreSQL commit. If any required pool rejects, all staged pool changes are discarded and only the negative receipt remains. Release verifies the original reservation decision and exact plan/project/task/period/seat ownership, removes only those temporal bookings and records its original result. Successful reserve replay after release does not reinsert bookings.

Tenant row security covers the booking table and receipt table. Composite foreign keys bind bookings to both their pool and exact tenant/project/baseline. Receipts permit runtime SELECT/INSERT only and have an ORM mutation guard. Temporal bookings are mutable ownership state; original evidence and effect decisions retain the history. `AllocatedSeats` continues to mean outstanding permanent allocations; `TemporalReservedPeak` is separately exposed by license detail.

## Planning observation and protocol compatibility

Scenario capture reads required license pool metadata and future bookings within its repeatable-read observation. Permanently allocated seats reduce the constant free capacity; captured temporal bookings reduce capacity only in their intervals. The independent validator includes existing bookings in its own sweep, separate from the search feasibility check.

The optional `LicenseBookings` input is omitted when null to preserve prior canonical JSON and scenario proof digests. Historical null-calendar input replays under `temporal/bounded-grid-cost-finish-v1`; explicit calendars use `temporal/bounded-grid-cost-finish-v2`. A fixture captured from the published d820e84 Domain binary checks original evidence bytes, digest and replay after the change. The model bounds captured license bookings at 5,000; the source query reads at most 5,001 before rejecting an oversized observation. Outstanding-booking growth and later cleanup/admission policies require measured G6/G7 work.

## Verification and remaining gates

The tests cover original success/rejection/release replay, retained SQL allocation IDs, rollback of a multi-pool rejection, concurrent identical effects, competing resource/seat intervals, adjacent project reservations, actual PostgreSQL policies, scenario capture/replay and 100 seeded capacity calendars against an independent discrete oracle. Deterministic abandoned-claim tests and the separate local process-kill lab exercise coordinator recovery; the [verification record](verification.md) describes the actually executed boundaries.

These participants do not consume materials, procure goods, authorize task starts or supersede a working baseline. The coordinator and task store own activation/execution admission. G4 supplies material/procurement participants and reconciliation of irreversible operations; G5 supplies adaptive supersession. The whole flagship is not declared complete by these increments.
