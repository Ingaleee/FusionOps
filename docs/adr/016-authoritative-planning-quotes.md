# ADR 016: Authoritative planning quotes and atomic acceptance

Status: accepted. Date: 2026-10-13.

## Problem

The pure planning preview accepts caller-supplied candidate rates, skills and calendars. An approved workflow currently accepts arbitrary resource IDs. Neither is an authoritative commercial offer.

## Decision

Introduce one SQL resource catalog with persisted skills, an active flag and a monotonic metadata revision. New allocations require a registered active resource. Preserve historical allocations using inactive imported catalog records; do not invent missing skills or commercial rates.

Calculate durable, immutable quotes from a bounded, explicit set of catalog candidates and actual overlapping bookings in one serializable SQL transaction. Store the request, candidate metadata, selected resource revisions, assignments, cost and solver certificate. Quotes expire after a bounded lifetime. Incomplete quotes explain shortages but cannot start a workflow.

The public workflow proposal references a quote ID. Project, period, resources, cost and quote digest come from the stored quote. Bind that identity and digest into the workflow specification hash. The approval view exposes the exact commercial offer. Preserve legacy persisted workflows for recovery.

Accept a quote at the SQL resource participant's commit boundary: lock the quote, lock resource rows in sorted order, verify identity, expiration and selected metadata revisions, then check calendars and persist allocations, quote consumption, the durable decision receipt and outbox together. One quote funds at most one workflow. Replay an existing successful receipt even after expiry or compensation; never recreate a released effect. Persist confirmed stale/expired/consumed decisions using the existing negative receipt protocol.

Catalog edits take the same resource locks and require an HTTP version precondition. Compensation can release allocations after resource deactivation. Historical reads and immutable quote evidence remain available.

## Guarantees and limits

The quote is optimal for its captured candidate set and calendars at calculation time. Acceptance guarantees the selected resource metadata and price, and exclusive availability at booking. It does not guarantee that changes to unselected candidates could not yield a cheaper new plan. Quote creation does not hold resources; expiry is checked at acceptance, not at approval. Cross-store proposal creation is not a distributed transaction: unused quotes are harmless; the SQL participant is the final authority.

Cross-store periods use microsecond precision. Sub-microsecond quote boundaries are rejected explicitly, rather than rounded into a different booked interval. Monetary totals are decimal and PostgreSQL quote amounts use unconstrained numeric storage.

The catalog is shared under existing resource management authorization. This milestone does not claim tenant isolation, project ownership or license price approval. Tenant authority must cover SQL, PostgreSQL, claims, receipts, audit and workers together in a later increment.

## Verification required

Real SQL tests for unknown/inactive resources, stale revisions, concurrent catalog edits/acceptance, competing quotes, atomic batch rollback, expiry, replay after lost reply and compensation after deactivation. HTTP tests must prove client data cannot override stored economics. Upgrade tests must preserve historical allocations without making anonymous resources eligible for booking.

## References

- [SQL Server table hints](https://learn.microsoft.com/en-us/sql/t-sql/queries/hints-transact-sql-table)
- [EF Core concurrency tokens](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)
