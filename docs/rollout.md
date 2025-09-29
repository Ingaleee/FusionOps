# FusionOps API Rollout & Monitoring

## Rollout

- Helm chart: `charts/fusionops-api`
- Rollout: Argo Rollouts (blue/green + canary)
- Services: `fusionops-api` (active), `fusionops-api-preview` (preview)
- Ingress: `api.example.com` (active), `preview-api.example.com` (preview)

## Как смотреть rollout

```sh
kubectl argo rollouts get rollout fusionops-api -n fusionops
kubectl argo rollouts promote fusionops-api -n fusionops
kubectl argo rollouts abort fusionops-api -n fusionops
```

## Monitoring

- Prometheus target: `app="fusionops-api"`, `rollout="..."`
- Grafana: latency p95, error rate, rollout status
- Alert: latency > 500ms, error rate > 1%

### Multi-tenancy SLIs

- Top-N tenants детализируются лейблом `tenant` (иначе агрегируй/хешируй).
- Примеры:
  - `rate(http_requests_total{tenant="acme"}[5m])`
  - `histogram_quantile(0.95, sum(rate(http_request_duration_seconds_bucket{tenant=~"(acme|zen)"}[5m])) by (le, tenant))`

### RLS Health

- Проверка старта: в логе `RlsInitializer` и отсутствие привилегии `BYPASSRLS` у юзера БД.
- Smoke: tenant A создаёт запись → tenant B не видит (см. `RlsIsolationTests`).

## SLO

- p95 latency < 500ms
- error rate < 1%

## Rollback

- Rollout abort → instant rollback
- История развёртываний видна в Argo Rollouts UI

## Keycloak mappers

- `tenant_id`: из первой части пути группы `/tenants/{tenant}/roles/...`
- `tenant_roles`: массив ролей арендатора (например, `Resource.Manager`, `Stock.Admin`)

