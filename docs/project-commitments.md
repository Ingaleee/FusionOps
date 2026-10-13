# Project commitments, graph revisions and membership

Projects are tenant-owned operational definitions with their own membership. A tenant Resource.Manager may create a project; the verified subject becomes its sole owner. Tenant manager roles alone do not grant access to another registered project. Project reads require membership; definition changes require Owner/Planner; planning requires Owner/Planner; approval requires Owner/Approver; operational changes require Owner/Operator; membership and ownership changes require Owner.

Production requires a registered project for existing allocation, quote, license allocation/release, workflow, scenario and project-audit operations. Anonymous Development retains unmanaged project IDs for earlier demonstrations. Registered Development projects enforce their membership normally. This transitional allowance is selected by the host environment, not a request header or mutable project option.

## Contract

Run `pwsh -File examples/project-demo.ps1` against the full Development API to inspect graph revisions, historical budgets and rejected changes. The signed-actor permission scenarios run in the production HTTP integration fixture.

| Route | Behavior |
| --- | --- |
| POST /api/v1/projects | `{id, definition}`; 201, Location and ETag; same original actor/specification can resolve its original submission without resetting the current project |
| GET /api/v1/projects?after={id}&limit=20 | Membership-scoped keyset page, 1–100 metadata summaries and caller roles; full graph evidence is fetched individually |
| GET /api/v1/projects/{id} | Current definition, membership, schema/digest, versions and conservative DAG time bounds |
| PUT /api/v1/projects/{id}/definition | Replace validated definition using current If-Match |
| PUT /api/v1/projects/{id}/members | `{subject, role}`; owner grants/revises one bounded exact subject |
| DELETE /api/v1/projects/{id}/members/{subject} | Owner removes a non-owner member using If-Match |
| POST /api/v1/projects/{id}/owner | `{subject}`; atomically transfers ownership to an existing member, retaining the previous owner as Planner |
| GET /api/v1/projects/{id}/revisions?before=2&limit=20 | Immutable definition history, descending keyset page, 1–50 rows |

If-Match must contain one quoted positive project version. Missing condition returns 428, malformed condition 400, stale version 412. Invalid definitions return 422 with a bounded rule code. Missing/foreign/nonmember project access returns 404. A changed original submission under an existing project ID returns 409. Definition/order-equivalent and same-role membership no-ops retain the current version and emit no new event.

There are two versions: Version advances for any accepted definition/membership/ownership change; DefinitionVersion advances only for a materially different canonical definition. Each definition revision preserves the project version at its creation. Concurrent mutations lock the parent row before checking permission/version and write state, evidence and ProjectChanged outbox event in one PostgreSQL transaction. Project creation also serializes first use by ID; a replay never resets subsequent definition or membership changes.

## Definition and graph

Definition schema 1 contains name, UTC horizon, budget amount/currency, sites and their explicit open intervals, tasks, and milestones. It supports 1–200 tasks, 1–32 sites, up to 64 milestones, up to 20 resource/material/license demands per task and up to 512 open intervals per site. Horizon is at most 366 days; timestamps must preserve microseconds; durations are positive integer minutes up to seven days. Supported currencies retain the repository's existing currency boundary.

Dependencies are finish-to-start edges between known distinct task IDs. The domain rejects cycles, repeated/unknown identities, duplicate dependency links, ambiguous skill names, invalid time windows, overlapping calendars and unbounded/invalid demand. An infeasible dependency-derived deadline remains valid planning input and appears in WindowConflicts/MilestoneConflicts. These time bounds ignore resources, calendars and procurement: they are conservative lower bounds, not a feasible execution plan or scheduling result.

Canonical evidence normalizes ordering, UTC offsets, skill names and decimal scale. The digest binds schema, tenant, project ID, definition version and exact canonical text. Reads return fresh deserialized values; mutating the caller's arrays or a returned definition cannot mutate retained evidence. Digests detect changed evidence; a privileged administrator can still replace both data and digest.

## Persistence, upgrade and revocation

Apply the additive PostgreSQL OperationalProjects migration before admitting traffic. New project, membership and revision tables have tenant row policies and composite ownership foreign keys. Runtime revision policies allow SELECT/INSERT only; there is no UPDATE/DELETE policy, including for the restricted worker role. The EF write guard also rejects changed/deleted revisions. Administrator migrations and restore tools remain trusted capabilities. No production endpoint deletes history.

The migration does not guess membership for historical project IDs. Register reviewed definitions and owners for those IDs before reopening production client operations. Existing approved reservation workers retain their persisted recovery protocol. A project definition does not yet bind a reservation to a temporal execution baseline; that arrives in G2/G3.

Membership is checked on operation admission. A revoked member cannot issue a later operation or read its old receipt through a protected endpoint. Already admitted work and already approved workflows are not automatically cancelled by revocation. Their authoritative effects and recovery remain durable. Plan/approval policy version binding and controlled execution cancellation are explicit G3 responsibilities.

See [flagship delivery](flagship-delivery.md), [tenant operations](tenant-isolation.md) and [reservation protocol](reservation-workflows.md).
