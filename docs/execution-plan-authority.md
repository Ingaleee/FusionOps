# Reviewed execution plan authority

G3: persisted immutable baselines, reviewed diffs and approval decisions. [Temporal participants](temporal-participants.md), [durable activation](execution-activation.md) and [task facts](execution-tasks.md) supply the supported workforce/equipment/license lifecycle. Adaptive baseline supersession remains G5 work. An `Approved` plan is reviewed evidence; capacity ownership requires its separate activation protocol.

## Evidence and state

`ExecutionBaseline` captures the original scenario identity, actor, request fingerprint, timestamp, digest and evidence JSON. Its digest binds tenant/project, definition identity/digest, plan/scenario/previous-plan IDs, proposer/time, review expiry, approval policy, costs/currency and diff. The independent temporal validator still checks the retained schedule. Partial scenarios cannot enter review. Hypothetical assumptions remain in retained evidence; approval does not convert them into real availability.

The original scenario, baseline and `ExecutionPlanDecision` rows are append-only for runtime PostgreSQL roles. They have tenant row policies for SELECT/INSERT only and ORM write guards. Composite foreign keys bind the exact tenant/project/scenario/definition/previous-plan relationship. The mutable `ExecutionPlan` row carries a concurrency version and approval summary. Evidence digests detect unintended mutation; they are not signatures against a malicious database owner capable of replacing data and digests.

The initial policy is `execution-approval/v1`: 24-hour review lifetime, 24-hour approval lifetime, and separate proposer/approver outside Development. The server pins this policy in the baseline; the caller cannot supply a weaker policy. Tenant-configurable policies are later G6 work. Approval lifetime limits future activation admission; it is not a command to reverse already committed effects.

State transitions are proposal → `AwaitingApproval` → `Approved` or `Rejected`; a planner can withdraw an awaiting/approved proposal. Rejection/withdrawal requires a bounded reason. Every successful transition appends one versioned decision and one `v1.ExecutionPlanChanged` outbox event in the same PostgreSQL transaction. A withdrawal retains the earlier approval facts. There is currently no active execution state or participant call from these routes.

## Admission and concurrency

Creation requires current project planning authority, matching project aggregate version and a scenario captured against that same current project/definition. An existing ID with the original actor/request returns its retained plan even after source/project revisions, provided the actor still has planning permission. A changed submission under the same ID returns 409. Current state may have advanced to approval/withdrawal; retry does not create a second baseline or decision.

Decision mutations require current project membership/permission, the plan ETag and the exact reviewed baseline digest. Approve/reject requires project `Approve`; withdrawal requires `Plan`. Definition changes invalidate approval admission for the earlier proposal. Membership-only changes are checked against current roles without changing the reviewed definition. Expired reviews, backdated domain transitions and reused decisions are rejected.

The store locks the project before the plan, matching project-role/definition mutation order. The parent lock prevents a role revocation or definition change from racing decision admission. Plan-row locking plus its concurrency version admits only one competing decision under an ETag. Read/history uses a repeatable-read transaction; pages select metadata without loading entire scenario evidence.

## API

All routes are under `/api/v1/projects/{projectId}/execution-plans`. Production requires verified authentication; writes also require `ManageResources`, in addition to the project-specific permission. Foreign/unavailable project reads and role-denied project operations return 404 under the existing project disclosure contract.

| Route | Contract |
| --- | --- |
| POST / | `{ id, scenarioId, expectedProjectVersion, previousPlanId? }`; 201 with Location, plan version ETag and retained proof/diff |
| GET /?after={id}&limit=20 | 1–100 metadata records; no evidence payload |
| GET /{id} | Current plan, original baseline evidence and ordered immutable decisions |
| POST /{id}/approve | `If-Match: "version"`, `{ reviewedDigest }`; reviewed-version approval |
| POST /{id}/reject | Current ETag, `{ reviewedDigest, reason }`; append rejection |
| POST /{id}/withdraw | Current ETag, `{ reviewedDigest, reason }`; append withdrawal |

Responses use `Cache-Control: no-store`. Missing ETag returns 428; malformed ETag returns 400; stale plan/project version returns 412; conflicting original submission returns 409; invalid digest/state/policy/expiry/partial scenario returns 422. A wrong or foreign scenario cannot be used to attach another project's evidence.

Diffs retain changed task intervals, assignments, modeled task cost and task-definition changes, plus finish/cost deltas and previous identity/digest. Currency changes are explicit and do not produce a subtraction across unlike currencies. Diff creation does not supersede an approved baseline, release capacity or reopen completed tasks.

## Execution boundary and remaining work

The Development walkthrough is `pwsh -File examples/execution-plan-demo.ps1 -BaseUri http://localhost:5121`. It includes the temporal fixture, reviewed-version approval and a changed plan requiring fresh review. Production uses signed identities and a separate approver, as exercised in the integration suite.

The coordinator now connects verified workforce/equipment/license intervals with fenced claims, dependency/start/completion facts and original participant recovery. Unsupported material activation is rejected until G4 supplies its participants; hypothetical additions must fit real capacity. A project may have multiple proposals but only one live activation. Controlled working-baseline supersession, irreversible work and financial closure remain later gates. The [verification record](verification.md) distinguishes deterministic abandoned claims, actual process-kill experiments and supported operator-reported task execution.
