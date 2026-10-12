# ADR 014: durable reservation idempotency

Status: accepted. Date: 2026-10-12.

## Problem

A reservation can commit while the HTTP response is lost. A blind retry otherwise looks like a second booking attempt and returns a conflict, leaving the client unable to distinguish success from failure. An in-memory cache cannot answer this after an API restart or on another replica.

## Decision

`POST /api/v1/allocate` accepts an optional `Idempotency-Key` header, 1–128 ASCII letters, digits, hyphens, underscores or dots. Requests without a key keep ordinary conflict semantics. The server scopes a key to the authenticated actor and the versioned operation `workforce.allocate/v1`.

The persisted scope is SHA-256 of the unambiguous JSON array `[actor, operation, key]`. Raw client keys are not persisted. The command fingerprint is SHA-256 of a canonical JSON object containing project ID, sorted resource IDs and start/end UTC ticks. Reordering resources or expressing the same instants in a different timezone does not change command identity. Different projects, resources or instants do.

The command pipeline order is **validation → logging → transaction → idempotency → handler**. Inside the owning SQL Server transaction, a transaction-owned `sp_getapplock` exclusive lock serializes the logical receipt key even on first use. Lock acquisition waits up to ten seconds; timeout/deadlock rejection returns 409 with a retry hint. Receipt lookup uses read-committed isolation under the logical lock rather than holding broad serializable gaps in a sparse receipt index. Reservation calendar reads retain their own serializable protocol.

On first execution, the handler's typed result is serialized and staged in `CommandReceipts`. The aggregate, outbox and receipt commit together. A handler or commit failure rolls back all three and releases the logical lock. No error response is cached.

On retry, a matching fingerprint returns the stored result without executing the handler or adding another event. A different fingerprint returns `409 idempotency_conflict`; the original receipt is unchanged. The API returns the original result body and 201/Location semantics on a successful replay. A new dependency-injection scope or API replica uses the same durable record.

## Guarantees and limits

- One committed reservation result per retained actor/operation/key, with atomic state/outbox/receipt persistence.
- Successful retries receive the original allocation IDs, including after process restart.
- Same client key belonging to another actor or operation has a different scope.
- Keys are currently retained indefinitely. There is no TTL or automatic deletion: expiration without a tombstone would permit accidental re-execution. A bounded retention policy needs a separate contract and operational design.
- The receipt replays the original command outcome, not the current allocation state. Later cancellation does not change the historic result.
- Authorization and validation still run on every HTTP request before replay. Idempotency is not an access token.
- Anonymous Development requests share the `system` actor. This is a local demo convenience, not tenant isolation. Tenant-aware scope and resource ownership remain separate milestones.
- This protects command execution; integration delivery remains at-least-once and still requires consumer deduplication.
- The store currently supports Workforce commands. Fulfillment commands do not claim this contract.
- Operation versions and response schemas must remain readable for the lifetime of retained receipts; changing the command's canonical representation requires a new operation version.

## Evidence

Fast tests cover canonicalization, invalid headers, conflict semantics, bypass without a key and failure before staging. Real SQL Server tests cover competing identical keys, fresh-scope replay, changed-payload conflict, actor scope separation, independent-key concurrency, commit rollback/retry and all-or-nothing multi-resource batches. The HTTP test follows the 201 Location to the created booking, verifies status/body/Location replay and checks `application/problem+json` on conflict.
