# Catalog, authoritative quotes and acceptance

The in-memory `/api/v1/planning/preview` remains a side-effect-free simulation. A commercial reservation uses the full API: persisted catalog → immutable quote → reviewed workflow → atomic SQL acceptance.

## Catalog API

All catalog and quote endpoints require Resource.Manager outside Development.

| Route | Behavior |
| --- | --- |
| POST /api/v1/catalog/resources | Register a unique human/equipment ID, name, rate, currency, skills and equipment type; 201, Location and ETag |
| GET /api/v1/catalog/resources/{id} | Read persisted metadata and revision; ETag |
| GET /api/v1/catalog/resources?limit=20&after={id} | Keyset page, 1–100 records |
| PUT /api/v1/catalog/resources/{id} | Replace metadata/active status using If-Match; 428 missing, 400 malformed, 412 stale |

Skills have case-insensitive unique names, bounded levels 0–5 and up to 20 entries. Rates use one supported currency, at most six decimal places and a ceiling of one million per hour. Every metadata update increments the revision, including restoring an old rate. Kind and resource ID are immutable. Deactivation blocks new reservations and leaves historical allocations available for compensation.

The catalog is the authority for new bookings. The SQL allocation foreign key rejects an unknown resource even if a caller bypasses the application repository. Activity and calendar admission require the locking protocol; direct database edits are outside that protocol. Legacy human/equipment tables remain read-only historical inputs for the old scenario preview, not the commercial quote source. Unclassified historical placeholders should remain inactive: if the real resource is equipment, register a correctly classified new ID rather than treating the placeholder's technical Human kind as historical evidence.

## Quote API

POST `/api/v1/planning/quotes` takes projectId, from, to, currency, demand slots, candidateIds and validitySeconds (15–900, default 300). It does not take commercial rates, skills or calendars. CandidateIds are an explicit bounded set of 1–200 resources; demand supports 1–50 slots. Time boundaries require microsecond precision, so the reviewed interval survives both database providers exactly.

Quote calculation locks catalog rows in sorted order and reads actual overlapping allocations within one serializable transaction. It runs the same Hungarian solver and stores the request, candidates and metadata revisions, relevant calendar evidence, assignments, rejections, exact decimal cost and primal/dual objectives. Creation does not reserve capacity. An incomplete quote is useful shortage evidence but cannot start a workflow.

The response contains id, digest, author, timestamps, consumption owner and evidence. GET `/api/v1/planning/quotes/{id}` reads the immutable evidence and checks its SHA-256 digest. Candidate and result snapshots cannot be mutated through a domain read. Digests detect changed evidence; they are not signatures against a privileged administrator who can rewrite the entire database.

## Review and accept

POST `/api/v1/workflows` now accepts workflowId, quoteId, licenses and completionTimeoutSeconds. This is a deliberate breaking replacement of the earlier caller-selected resource proposal. Project, period, selected resources and reviewed price are taken from the stored quote. The workflow includes the quote ID, digest, exact amount/currency and expiry; these values enter its immutable specification hash. Existing persisted unquoted workflows retain their old fingerprints and remain recoverable.

Approval still requires If-Match, both manager roles and a separate production actor. Approval records consent; actual resource acceptance occurs in SQL Server. The resource participant locks the quote and selected resources, verifies evidence/binding, checks revisions and calendars, then checks expiry with the SQL database clock immediately before saving. It commits allocations, consumedByWorkflowId, decision receipt and outbox together. A conflict rolls back the whole batch and consumption. A durable negative receipt prevents a delayed worker from turning the rejected decision into a later booking.

| Failure code | Meaning / next action |
| --- | --- |
| resource_unknown / resource_inactive | Register or review the resource before a new attempt |
| catalog_changed | The reviewed metadata changed; calculate and review a new quote |
| resource_conflict | Capacity was taken after calculation; replan |
| quote_expired | Acceptance occurred after the quote lifetime; calculate a new quote |
| quote_consumed | Another workflow already accepted this quote; calculate a new quote |
| quote_evidence_invalid / quote_binding_mismatch | Stored evidence or workflow binding disagrees; investigate persisted state |
| quote_incomplete | Some demand remains unfilled; revise demand/candidates before proposing |

Existing successful receipts take precedence over expiration, metadata edits and compensation: a retry resolves the original outcome without booking again. Compensation does not make a consumed quote reusable. A different workflow always needs a new quote.

The quote is optimal for its captured candidate set at calculation time. Acceptance guarantees the selected metadata and exclusive availability, not continued global optimality if unselected resources later become cheaper. The approved price covers workforce only; license pricing is a future contract.

## Upgrade and scope

Apply both context migrations before enabling the API. The SQL migration copies legacy humans/equipment into inactive catalog rows, preserves their recorded names/rates and creates inactive placeholders for historical allocation IDs. Unknown historical kind/rate is explicitly unreviewed: the placeholder uses Human/USD 0 solely to retain the foreign key and historical recovery. No placeholder is eligible for new planning. Legacy skills were never persisted and cannot be reconstructed. Review metadata and activate with If-Match before booking.

If a legacy human and equipment share an ID, migration fails and rolls back with SQL error 51000; resolve the collision explicitly. The PostgreSQL migration adds nullable quote binding fields and preserves unquoted workflows. Retain quote evidence and receipts indefinitely until a documented retention/tombstone policy exists; deleting history can invalidate recovery and commercial audit.

Catalog, quotes, calendars and reservation workflows now belong to the verified request tenant. Tenant identity enters new quote/workflow fingerprints; composite foreign keys and database row policies enforce ownership through acceptance and compensation. Existing records belong to the documented legacy tenant and retain their original fingerprints. See [tenant contract](tenant-isolation.md) and [ADR 017](adr/017-tenant-boundaries-and-row-security.md).

Registered projects enforce membership and planning permissions as described in [project authority](project-commitments.md). Temporal execution baselines, multi-period optimization and measured capacity/SLOs remain open architecture work. [ADR 016](adr/016-authoritative-planning-quotes.md) records the original quote protocol; [verification evidence](verification.md) distinguishes local checks from published CI results.
