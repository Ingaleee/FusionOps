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
