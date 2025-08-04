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

## SLO

- p95 latency < 500ms
- error rate < 1%

## Rollback

- Rollout abort → instant rollback
- История развёртываний видна в Argo Rollouts UI
