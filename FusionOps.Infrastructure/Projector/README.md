# FusionOps AuditProjector

Сервис для проекции событий аудита из EventStoreDB в Postgres (allocation_history).

## Запуск локально

```
dotnet run --project FusionOps.Infrastructure/Projector
```

## Docker

```
docker build -t fusionops-auditprojector .
docker run --network=host fusionops-auditprojector
```

## Метрики

- `/metrics` — Prometheus-compatible, метрика `projection_lag_seconds`

## Переменные окружения

- `ConnectionStrings__Postgres` — строка подключения к Postgres
- EventStoreDB — через esdb://eventstore:2113?tls=false

## Масштабирование

- Можно запускать несколько экземпляров (idempotency)

## Мониторинг

- Prometheus scrape `/metrics`
- Alert: если `projection_lag_seconds > 30` — сигнализировать о задержке