CREATE TABLE IF NOT EXISTS allocation_history (
  id            UUID PRIMARY KEY,
  allocation_id UUID,
  project_id    UUID,
  resource_id   UUID,
  from_ts       TIMESTAMP,
  to_ts         TIMESTAMP,
  recorded_at   TIMESTAMP,
  sku           TEXT,
  qty           INT
) PARTITION BY RANGE (recorded_at);

-- Пример партиции на август 2025
CREATE TABLE IF NOT EXISTS allocation_history_2025_08 PARTITION OF allocation_history
  FOR VALUES FROM ('2025-08-01') TO ('2025-09-01');
