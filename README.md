## Observability quick demo

```bash
# Port-forward Prometheus & Grafana from k8s preview namespace
kubectl port-forward svc/prometheus-server 9090:9090 -n monitoring &
kubectl port-forward svc/grafana 3000:3000 -n monitoring &
```

![Grafana lag alert](docs/images/grafana_kafka_lag.gif)

The gif shows Kafka consumer lag spike and alert firing. 