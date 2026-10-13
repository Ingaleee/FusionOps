# Governed temporal activation

G3 increment: reviewed plans have a durable activation coordinator and HTTP/hosted-worker path. Activation acquires workforce/equipment task intervals and future license seats through the [owning participants](temporal-participants.md), then permits [controlled task facts](execution-tasks.md). Working-plan supersession remains G5 work. `Active` means confirmed reservation activation; `Completed` means all baseline tasks reported complete and original ownership released, without implying financial closure or physical-provider certification.

## Admission and ownership

An operator submits the exact reviewed digest and approved plan ETag. Under the project parent lock, admission verifies current Execute permission, the approver's current Approve permission, approval state/expiry, immutable baseline proof and the current definition version/digest. Materials are explicitly unsupported until G4; activation after the earliest planned start is rejected. Hypothetical capacities never create ownership: participants check actual stores.

The baseline ID is the stable activation identity. Replay with the original actor/digest/approved version returns its retained current activation, including a compensated/rejected result; it does not reacquire capacity. A different request under that identity conflicts. The project lock and a tenant/project partial unique index permit only one live activation, including partial reservation, compensation and unresolved states. Supersession is not implemented by creating another activation.

The coordinator executes resources, then licenses. Resource success retains task/resource/allocation mappings. A resource rejection ends admission; a license rejection releases only the earlier confirmed resources. Cancellation during a pending reserve first resolves its original result, then releases confirmed owners in reverse order. Timeouts remain unknown; retries use the same durable participant decision. An unresolved step exhausts its bounded retries into `NeedsAttention`, retaining its exact paused step and project ownership until an authorized operator resumes it.

Worker claims use short PostgreSQL database-clock leases, token/epoch, `FOR UPDATE SKIP LOCKED` and bounded participant deadlines. Completion rechecks token, epoch, current step and unexpired lease under locks. A stale worker cannot record coordinator progress. Original participant receipts prevent a late process from creating another effect. Before each confirmed reservation advances, current definition/approval/operator/approver authority is rechecked; invalid authority drives ownership compensation rather than Active. Plan withdrawal requires activation compensation first. Revocation does not erase committed external-store ownership. Once a task starts, generic cancellation is refused; completing all tasks requests ownership release with a Completed outcome instead of compensation.

Mutable coordinator state, one append-only decision and its typed outbox event commit together. Decisions retain actor/reason/state/error/paused step and lease epoch. PostgreSQL row policies cover both state and history; history permits runtime SELECT/INSERT only and has an ORM mutation guard. System workers need the existing trusted worker/database role boundary. A database administrator remains trusted.

## API

Routes are under `/api/v1/projects/{projectId}/execution-plans/{id}/activation`. Production requires verified identities; writes require `ManageResources` plus project Execute permission. Reads require project Read permission. Foreign/unavailable projects return 404.

| Method / suffix | Contract |
| --- | --- |
| POST / | `{ reviewedDigest }`, approved **plan** ETag; 202 plus activation Location/current activation ETag |
| GET / | Activation, original resource mappings and ordered decisions |
| POST /cancel | `{ reason }`, current **activation** ETag; request ownership compensation |
| POST /resume | `{}`, current **activation** ETag; resume the retained unresolved step |

ETags for review and activation belong to distinct aggregates. Missing ETag is 428; stale version 412; identity/project-owner conflict 409; invalid state/digest/approval/unsupported demand 422. Responses are no-store. `Execution:WorkerEnabled` controls the hosted activation worker; metrics record step outcomes, fenced completions and duration without advertising capacity.

## Evidence and remaining limits

Domain and real-store tests cover concurrent plans of one project; actual task-interval ownership; negative license decisions and compensation; cancellation during a committed license effect; abandoned worker after both resource and license commits; lease replacement and stale completion rejection; role revocation after both commits; unresolved result/operator resume; real signed HTTP/ETags; hosted worker; actual restricted PostgreSQL policies.

The abandoned-worker tests discard a result after the real participant commit and expire its lease deterministically. They are distinct from the OS-killed worker experiment in `scripts/verify-execution-recovery.ps1`. The latter uses a Development-only commit probe, kills only its labeled API container after actual confirmation, restarts it with ordinary configuration, waits for lease replacement and verifies activation/compensation with retained mappings. See the verification record for actual results. No HTTP route enables the probe; production configuration refuses the failure lab.

`examples/execution-activation-demo.ps1` exercises the Development API/worker and compensation; `examples/execution-task-demo.ps1` adds operator-reported dependency execution and ownership closure. Irreversible effects, supplier operations, adaptive supersession and project cost closure remain later gates.
