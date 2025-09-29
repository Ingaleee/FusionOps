# FusionOps GraphQL Gateway

## Как подключиться

- Endpoint: `https://gateway.local/graphql`
- Auth: JWT (Authorization: Bearer ...)

## Пример Query

```graphql
query {
  allocation(projectId: "...uuid...") {
    allocationId
    resourceId
    from
    to
  }
}
```

## Пример Subscription

```graphql
subscription {
  lowStock {
    sku
    qtyLeft
    expectedDate
  }
}
```

## Пример curl

```sh
curl -H "Authorization: Bearer <token>" \
     -X POST https://gateway.local/graphql \
     -d '{"query":"{ allocation(projectId:\"...\"){ allocationId } }"}'
```

## Как деплоить

- Helm chart: `charts/fusion-gateway`
- Argo Rollouts: blue/green, canary, p95 analysis
- CI/CD: `.github/workflows/gateway-ci.yml`

## Мониторинг

- Prometheus scrape `/metrics`
- Grafana: latency p95, error rate, subscriptions

## Rollout

- `kubectl argo rollouts list`
- `kubectl argo rollouts promote fusion-gateway`

## Multi-tenancy

- Tenant resolution: из JWT `tenant_id`, поддомена `{tenant}.gateway.local`, либо заголовка `X-Tenant-Id` (для внутренних вызовов).
- Пробрасываем `tenant_id` в backend сервисы как заголовок и контекст, сохраняем консистентность с API.
- Policy: для GraphQL резолверов используйте политики `T:*` (например, `T:AdminStock`) на основе клейма `tenant_roles`.

### Примеры

```sh
curl -H "Authorization: Bearer <token-with-tenant_id=acme>" \
     -H "X-Tenant-Id: acme" \
     -X POST https://acme.gateway.local/graphql \
     -d '{"query":"{ allocation(projectId:\"...\"){ allocationId } }"}'
```

### Keycloak

- Protocol mappers:
  - `tenant_id` (из группы `/tenants/{tenant}/roles/...`)
  - `tenant_roles` (массив ролей для текущего арендатора)
