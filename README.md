## FusionOps — Production‑grade ресурсный бэкенд с аудитом, GraphQL‑BFF и GitOps деплоем

### Что это за проект и зачем он нужен
FusionOps — это эталонная промышленная платформа управления операциями (operations) для компаний, которым критичны прослеживаемость, надёжность и скорость изменений. Платформа решает задачи распределения ресурсов (люди/оборудование), учёта складских остатков и аудита бизнес‑событий, предоставляя единый API/GraphQL‑слой для фронтов и автоматизированный продакшен‑деплой.

Проект демонстрирует, как собрать «облачный» бэкенд уровня senior: от доменной модели и CQRS до EventStore‑аудита, realtime, GitOps‑деплоя с канареечным анализом и автоскейлом по фактической нагрузке.

### Для кого
- **Операционные команды (Ops/Logistics/Manufacturing)** — планирование и отслеживание аллокаций ресурсов и запасов в реальном времени.
- **Продукт/Инженерия** — быстрые фронты на GraphQL, прозрачный аудит изменений, стабильные раскатки.
- **Безопасность/Compliance** — неизменяемый журнал событий (ESDB), RBAC, политики кластера, сканы образов.

### Что платформа умеет
- **Распределение ресурсов**: бронь/отмена с защитой от пересечений, возврат свободных слотов, алгоритмический подбор (Hungarian/GREEDY).  
- **Склад и запасы**: учёт остатков, reorder‑события, пополнения, realtime‑уведомления о low‑stock.  
- **Аудит и регуляторика**: каждый доменный и бизнес‑ивент заворачивается в `AuditEnvelope` и хранится в EventStoreDB; проекции в Postgres для быстрых выборок и отчетов.  
- **Realtime и интеграции**: SignalR‑хаб (уведомления о событиях), GraphQL‑Gateway (Query/Subscription), CLI‑tool для автоматизации.  
- **Надёжные релизы**: Argo Rollouts (Blue/Green/Canary) с автоматическим анализом p95 и error‑rate, preview трафик, безопасный promote/abort.  
- **Масштабируемость**: KEDA автоскейлит consumer‑ы по глубине очередей/лагу (RabbitMQ/Kafka).  
- **Наблюдаемость**: OpenTelemetry метрики/трейсы → Prometheus/Grafana; health/liveness; алерты на деградацию.  
- **Безопасность**: JWT (Keycloak), CSP/HSTS, Gatekeeper политики (no‑privileged), Trivy сканы, опционально cosign/SBOM.  

### Ключевые пользовательские сценарии (E2E)
- Менеджер ставит задачу: «Забронировать станок и бригаду на окно завтра 10:00‑14:00» → API валидирует пересечения, создаёт аллокации, шлёт событие; фронт видит обновление по SignalR; аудит фиксирует «кто/что/когда».
- Склад оповещает о падении остатка ниже порога → realtime `lowStock` событие в фронт, возможен автозаказ; аудиторская запись готова для отчёта.  
- Выпуск новой версии API/Gateway: раскатка в preview, 10% трафика и автоматический анализ p95; при норме — promote; при регрессе — автоматический abort/rollback.

### Чем отличается от «обычных» шаблонов
- Не просто CRUD: демонстрирует событийность (EventStoreDB), проекции, realtime и strong DevOps практики.  
- Продакшен‑готовые механики: Rollouts с метриками, KEDA, Gatekeeper, security headers, CI‑сканы, Chaos‑эксперименты.  
- Чистая архитектура: Application не зависит от Infrastructure, быстрые проекции через LINQ/CompiledQuery, разделение write/read concerns.

---

### Содержание
- [TL;DR: как запустить локально](#tldr-как-запустить-локально)
- [Архитектура](#архитектура)
- [Слои и ключевые компоненты](#слои-и-ключевые-компоненты)
- [API и контракты](#api-и-контракты)
- [GraphQL Gateway](#graphql-gateway)
- [Аудит и проекции](#аудит-и-проекции)
- [Наблюдаемость (Observability)](#наблюдаемость-observability)
- [Деплой и GitOps](#деплой-и-gitops)
- [Автоскейл по очередям (KEDA)](#автоскейл-по-очередям-keda)
- [Безопасность](#безопасность)
- [Хаос-инжиниринг и пен‑тест](#хаос-инжиниринг-и-пен-тест)
- [Тестирование](#тестирование)
- [Структура репозитория](#структура-репозитория)

---

### TL;DR: как запустить локально
1) Зависимости: Docker, .NET SDK 9, kubectl/helm (для k8s), Node не требуется.

2) Инфраструктура (EventStoreDB, Postgres, RabbitMQ, Keycloak при необходимости):
```bash
# из корня
docker compose up -d
```

3) Запуск API и Gateway локально:
```bash
# API
dotnet run --project FusionOps.Presentation
# Gateway
dotnet run --project FusionOps.Gateway
```

4) Проверка:
- REST: http://localhost:5000/swagger (в Dev) / http://localhost:5000/health
- GraphQL: http://localhost:8080/graphql (schema‑first)
- SignalR: ws://localhost:5000/hubs/notify
- Метрики API: http://localhost:5000/metrics

5) Быстрый e2e:
- POST /api/v1/allocate → событие уходит в ES → Projector вносит запись в Postgres → GET /api/v1/audit/allocations/{projectId}

---

### Архитектура
```mermaid
graph LR
  subgraph Client
    UI[SPA/Apps]
    CLI[dotnet-fusion]
    GQL[GraphQL Client]
  end
  subgraph Gateway
    GW[HotChocolate GraphQL]
  end
  subgraph API
    API[ASP.NET Core Minimal API]
    HUB[SignalR Hub]
  end
  subgraph Data
    ESDB[EventStoreDB]
    PG[(PostgreSQL)]
    SQL[(SQL Server)]
    RBQ[(RabbitMQ)]
  end

  UI -->|HTTP/WS| GW
  GQL -->|GraphQL| GW
  CLI -->|REST/WS| API
  GW -->|REST + JWT| API
  API --> HUB
  API --> SQL
  API --> RBQ
  API --> ESDB
  ESDB -->|Subscription| Projector
  Projector --> PG

  classDef box fill:#0b5cff,stroke:#333,stroke-width:1,color:#fff;
  class GW,API,HUB,Projector box;
```

---

### Слои и ключевые компоненты
- `FusionOps.Domain`: сущности, ValueObjects, Domain Events (`[EventType]` для версионирования)
- `FusionOps.Application`: MediatR UseCases/Queries, валидация, pipeline (`Validation → Logging → Transaction → Audit`)
- `FusionOps.Infrastructure`: EF Core (SQL/PG), Outbox, репозитории, Projector (ES→PG)
- `FusionOps.Presentation`: Minimal API, SignalR, middleware (CSP/HSTS, Correlation, Exception)
- `FusionOps.Gateway`: HotChocolate GraphQL, DataLoader, SignalR bridge; прокидывает JWT к API

---

### API и контракты
- Аллокации (батч для Gateway):
  - `GET /api/v1/projects/allocations?ids={id1,id2,...}` → `Dictionary<Guid, AllocationDto[]>`
- Аудит (read side):
  - `GET /api/v1/audit/allocations/{projectId}` → `PagedResult<AllocationHistoryDto>` + фильтры `from/to/page/pageSize`
- Realtime:
  - SignalR Hub: `/hubs/notify` (события `allocationUpdate`, `lowStock`, `ResourceAllocated`, `StockReplenished`)

DTO примеры:
```json
// AllocationDto
{ "allocationId": "...", "resourceId": "...", "from": "2025-08-08T12:00:00Z", "to": "2025-08-08T16:00:00Z" }

// AllocationHistoryDto
{ "allocationId":"...","resourceId":"...","from":"...","to":"...","recorded":"..." }
```

---

### GraphQL Gateway
- Schema‑first (`FusionOps.Gateway/Schema.graphql`), HotChocolate сервер
- DataLoader батчит REST вызовы к API (`/api/v1/projects/allocations`)
- Подписки: lowStock через SignalR, прокидка JWT в REST вызовы

Пример запроса:
```graphql
query GetAllocations($project: UUID!) {
  allocation(projectId: $project) { allocationId resourceId from to }
}
```

---

### Аудит и проекции
- Все Domain Events оборачиваются в `AuditEnvelope<T>` и пишутся в EventStoreDB
- Projector (BackgroundService) читает ES и вставляет в `allocation_history` (Postgres), idempotency `ON CONFLICT DO NOTHING`
- Таблица партиционируется по `recorded_at`; индексы `(project_id, recorded_at)`

---

### Наблюдаемость (Observability)
- Traces: OpenTelemetry + Jaeger (Dev)
- Metrics: OpenTelemetry Prometheus Exporter, endpoint `/metrics`
- PromQL в Argo Rollouts использует OTel гистограммы p95

Быстрый просмотр локально:
```bash
kubectl -n monitoring port-forward svc/prometheus-server 9090:9090 &
kubectl -n monitoring port-forward svc/grafana 3000:3000 &
```

---

### Деплой и GitOps
- Helm charts: `charts/fusionops-api`, `charts/fusion-gateway`
- Argo Rollouts (Blue/Green/Canary) с analysis (p95 / error‑rate), preview service

Установка:
```bash
helm upgrade --install fusion-api charts/fusionops-api \
  --set image.repository=ghcr.io/yourorg/fusionops-api \
  --set image.tag=$(git rev-parse --short HEAD)

helm upgrade --install fusion-gateway charts/fusion-gateway \
  --set image.repository=ghcr.io/yourorg/fusion-gateway \
  --set image.tag=$(git rev-parse --short HEAD)
```

Промоут в Rollouts UI или CLI:
```bash
kubectl argo rollouts get rollout fusionops-api
kubectl argo rollouts promote fusionops-api
```

---

### Автоскейл по очередям (KEDA)
- Пример `ScaledObject` по длине RabbitMQ очереди `outbox`
- Включение: `values.yaml` → `enableKeda: true`, `Rabbit__ConnectionString`

---

### Безопасность
- JWT Keycloak (RBAC: `Audit.Read`, роли `AuditViewer`, `Admin`)
- Security headers: HSTS, CSP (строгие источники, WS/SignalR учтён)
- Gatekeeper: запрет привилегированных контейнеров, расширяемые политики
- Trivy image scan в CI (фейл на CRITICAL)

---

### Хаос-инжиниринг и пен‑тест
- Litmus `pod-delete` RabbitMQ (staging, nightly)
- ZAP full‑scan (staging), фейл на High severity

---

### Тестирование
- Unit/Integration в `FusionOps.*.Tests`
- E2E: allocate → projector → audit endpoint
- Testcontainers для ES/PG — изоляция и реалистичность

---

### Структура репозитория
- `FusionOps.Domain/` — модель домена, события, ValueObjects
- `FusionOps.Application/` — UseCases/Queries, пайплайны, валидаторы, DTO
- `FusionOps.Infrastructure/` — EF, репозитории, Projector, Outbox, Messaging
- `FusionOps.Presentation/` — Minimal API, SignalR, middleware, Program
- `FusionOps.Gateway/` — GraphQL BFF, DataLoader, SignalR bridge
- `charts/` — Helm charts для API и Gateway (Rollouts, AnalysisTemplates)
- `docker/` — инициализация БД и служб для локалки
- `.github/workflows/` — CI/CD, security scans, chaos, ZAP

---

Если нужна быстрая демонстрация/видео — см. `docs/rollout.md` и `docs/gateway.md` (пример запросов/подписок, Rollouts promote/abort). Вопросы/идеи/улучшения приветствуются — проект задуман как эталонный «скелет» для прод‑платформы. 