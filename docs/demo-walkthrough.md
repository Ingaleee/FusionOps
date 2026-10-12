# A ten-minute engineering walkthrough

This walkthrough demonstrates implemented guarantees. Start with the standalone planning demo, then exercise concurrency and failure recovery on disposable infrastructure.

## 1. Explain the operational problem

There are two demand slots: a general operator and a C# specialist. Candidates differ in skills, cost and availability. A cheap specialist is already booked; a greedy choice can consume the only feasible specialist for the general slot.

Run the Development planning demo and submit `examples/specialist-plan.json`, using the README commands. Inspect the response: USD 60 total for two hours, two feasible assignments, candidate rejection reasons and equal primal/dual objective. Change a required skill or a busy interval to see an unfilled slot instead of an invented feasible assignment.

The objective first maximizes filled slots and then minimizes cost. Forbidden edges represent infeasible assignments. Private dummy candidates model unfilled demand; their penalty dominates feasible real costs. Certificates apply to that augmented optimization problem. A partial plan's augmented objective is not its payable resource cost.

Code to discuss: `HungarianAssignmentSolver`, `ResourcePlanner`, `ResourcePlannerTests` and `AssignmentSolverTests`. The seeded exhaustive verification checks optimality across sparse matrices with different shapes.

## 2. Explain the reservation commit

In full mode, use `examples/fusionops.http`. Send a reservation with an Idempotency-Key, then repeat it. The second request returns the original allocation IDs and the same 201 Location. Follow that Location to read the project's bookings. Change the project or time while retaining the completed key: the API returns 409.

Validation and authorization precede replay. The actor, operation version and client key define the durable scope. A logical SQL application lock serializes concurrent uses of that scope. A canonical fingerprint ignores resource order and timezone notation while preserving command meaning.

The command transaction encloses calendar reads, state changes, outbox staging and receipt staging. Nothing outside that transaction can turn an uncommitted result into a successful replay. When a later resource in a batch conflicts, the entire batch rolls back, including its receipt and events.

Code to discuss: `TransactionBehavior`, `IdempotencyBehavior`, `SqlCommandReceiptStore`, `AllocationRepository` and `EfUnitOfWork`. Explain why locking only existing allocation rows would miss the empty-calendar case, and why sorting resource IDs prevents opposite lock acquisition order.

## 3. Explain the failure window

The API commits before external delivery. A dispatcher writes audit, publishes the typed RabbitMQ event and attempts a notification. It can crash after publication but before its database commit. A retry can therefore publish again.

This is at-least-once delivery. The stable original event ID allows a consumer inbox to deduplicate; it does not itself implement that inbox. Audit append verifies existing identity and content. The projector inserts by event ID and commits its source checkpoint with the projection row, so replay does not duplicate history or skip a failed recognized event.

Poison messages enter durable backoff and then dead-letter state. Independent eligible events continue. Discuss the current tradeoff: network delivery holds a row lock and connection, bounded by a ten-second attempt. A lease protocol should be justified by measurements and would require fencing and crash recovery.

Code to discuss: `OutboxProcessor`, `EventStoreAuditWriter` and `AuditProjector`. Run the reliability suite to observe concurrency, rollback, poison events, real RabbitMQ identity and checkpoint replay on disposable infrastructure.

## 4. Demonstrate an approved cross-store workflow

Run `examples/workflow-demo.ps1` against the full Development API. A proposal has no resource side effects until approved with its ETag. The background worker reserves workforce and licenses, then the script cancels the successful workflow. A second workflow demonstrates license-capacity rejection and workforce compensation.

Open the workflow state and ordered transition history. Explain the ambiguous window: a participant can commit while the worker loses its reply. Replaying the stable operation returns its stored outcome. A confirmed rejection is retained too, so a stale worker cannot later create an effect after compensation. Compensation identifies the original resource IDs and workflow-owned license reference.

The worker commits a short claim before participant I/O. Its token, epoch and database-clock lease fence stale completion. Cancellation in flight is durable intent and waits for the participant's recorded outcome. NeedsAttention preserves the unresolved step for operator resume. See ADR 015 and the workflow runbook for the protocol.

Code to discuss: `ReservationWorkflow`, `ReservationWorkflowRunner`, `PostgresReservationWorkflowStore`, `SqlWorkforceWorkflowParticipant`, `PostgresLicenseWorkflowParticipant` and `WorkflowReliabilityTests`. Tests inject lost replies after both reservation and compensation commits, and verify isolation of another workflow's license ownership.

## 5. Show evidence and state limits

Use `docs/verification.md` for the actual checks and `docs/adr` for design decisions. Show the CI workflow and lock files. Start the planning demo from a clean clone rather than relying on a developer database.

Be explicit about the next architectural steps: catalog/tenant ownership, general consumer inbox, stock shipment compensation, authoritative planning snapshots and measured SLOs. Direct SQL writes are outside the reservation locking protocol. Disconnected SignalR clients do not have a durable subscription. No performance number or production-readiness claim is supported until measured.
