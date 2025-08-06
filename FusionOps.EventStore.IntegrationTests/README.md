# FusionOps.EventStore.IntegrationTests

Интеграционные и e2e тесты для аудита аллокаций через EventStoreDB и allocation_history.

## Как запускать

```sh
dotnet test FusionOps.EventStore.IntegrationTests
```

## Что тестируется

- allocate → EventStoreDB (интеграционный)
- allocate → allocation_history (e2e, через projector)

## Требования

- Docker (Testcontainers стартует EventStoreDB и Postgres автоматически)
- Для e2e: projector и приложение должны уметь брать строки подключения из переменных окружения

## Пример теста

- `AllocateToEventStoreTest` — проверяет, что после allocate появляется событие в EventStoreDB
- `AllocateToHistoryE2ETest` — проверяет, что после allocate строка появляется в allocation_history