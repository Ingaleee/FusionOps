# ADR-008: Change Data Capture (CDC) vs Polling for Outbox/Event Streaming

Date: 2024-05-01

## Context
Downstream analytics & ML services require near-real-time changes from SQL Server tables.
Options:
1. **CDC via Debezium** reading transaction log.
2. **Row-level polling** cron job querying `updated_at`.

## Decision Drivers
* Latency (seconds vs minutes).
* Load on primary DB (log-tailing vs SELECT).  
* Exactly-once semantics (offset vs duplicates).
* Operational complexity (additional services).

## Decision
Adopt **CDC with Debezium** (binlog tailing).

## Consequences
+ Near-real-time Kafka topics (millisecond lag).
+ Low read contention on OLTP tables.
+ Requires Kafka Connect cluster, schema-registry.
+ Must manage transaction log retention to avoid bloat. 