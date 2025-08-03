# ADR-011: GraphQL Gateway (HotChocolate BFF)

## Context

- Требуется единая точка входа для фронта (query + subscriptions)
- REST + SignalR неудобно для фронта, нет batching, нет schema
- Требуется production-grade observability, canary, rollbacks

## Decision

- Используем HotChocolate (C#) как BFF/gateway
- DataLoader для batching, resilient REST через Polly
- SignalR bridge для subscriptions
- JWT security, RBAC, CORS
- Helm chart, Argo Rollouts, Prometheus, Grafana
- E2E smoke-тесты, CI/CD pipeline

## Alternatives

- Apollo Router (Node.js) — не native для .NET, сложнее интеграция с SignalR
- YARP + REST — нет schema, нет subscriptions, нет DataLoader

## Consequences

- Фронт получает /graphql endpoint (query + subscriptions)
- Можно расширять federation, добавлять новые сервисы
- Production-ready: zero-downtime deploy, observability, rollbacks
