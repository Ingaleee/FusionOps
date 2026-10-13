# ADR 018: versioned project commitments and project membership

Status: accepted, 2026-10-13.

## Context

Authoritative quotes and recoverable reservations used opaque project IDs. Tenant ownership prevented cross-tenant access, but tenant manager roles exposed all project operations inside a tenant. A temporal planner also needs an authoritative dependency graph, calendars and budget whose revision can be reviewed and replayed.

## Decision

Introduce a tenant-owned OperationalProject in PostgreSQL with a validated canonical definition, versioned membership and immutable definition revisions. A definition contains bounded task/site/milestone graphs and explicit UTC windows; task resource/material/license requirements prepare the temporal planning contract. Kahn ordering rejects cycles and computes conservative dependency time bounds. Infeasible deadlines are reported as planning conflicts instead of silently inventing feasibility.

Keep aggregate Version separate from DefinitionVersion. Membership/ownership changes advance the former; a materially changed definition advances both and appends one evidence row. Canonical ordering, UTC normalization, skill-name normalization and decimal-scale normalization preserve equivalent submissions. The digest includes schema, tenant, project ID and definition version. Original creation identity remains stable through later revisions and grants.

Every project mutation holds the parent row lock, checks current project permission and ETag, then commits state, optional new revision and the original ProjectChanged outbox event together. Same original creation can return the current aggregate without resetting it. No-op changes emit no new event. Ownership transfer is one transaction and preserves exactly one domain owner.

Centralize application project permissions through IProjectAccess. The public allocation/quote/license/workflow/audit/scenario paths check this boundary in addition to their tenant and transport role policies. Project roles distinguish reading, planning/editing, approval, execution and membership administration. Tenant managers gain no automatic membership. Reads return 404 for absent/nonmember/foreign projects.

Apply database row security and composite tenant/parent keys to projects, members and revisions. Revision tables grant SELECT/INSERT through policies and have no runtime UPDATE/DELETE policy. EF also forbids tracked revision changes. Project membership authorization remains an application boundary; the shared database credential is a trusted server capability, as documented in ADR 017.

## Migration and limits

Existing opaque project IDs receive no inferred owner. Production client operations require explicitly registered reviewed projects; Development permits unmanaged legacy IDs for existing examples. Already approved workers retain their recorded execution/recovery protocol. Deployment must register appropriate definitions/owners for historical IDs before restoring production operator access.

Authorization is checked on operation admission; concurrent or later revocation does not retroactively erase an admitted effect or automatically cancel an approved workflow. G3 will bind execution baselines and policy epochs explicitly. The G1 task graph is definition evidence, not a temporal resource schedule. Calendars are explicit UTC intervals; recurring/time-zone expansion, started/completed-task preservation, procurement execution and budget commitment accounting are subsequent milestones.

## Evidence

Domain scenarios verify canonicalization, immutable snapshots, cycle/identity/calendar rejection, an explicit permission matrix, ownership transfer and 120 seeded DAGs against independent longest-path enumeration. Real PostgreSQL/HTTP scenarios use signed JWTs to check project isolation, current-version races, original creation replay, endpoint permissions and append-only evidence under a restricted non-bypass role. See the verification record for completed runs.
