-- Индекс для быстрого поиска по проекту и дате записи
CREATE INDEX ix_history_project_rec ON allocation_history(project_id, recorded_at DESC);

-- Индекс для фильтрации по датам
CREATE INDEX ix_history_recorded ON allocation_history(recorded_at DESC);

-- Составной индекс для фильтрации по проекту и диапазону дат
CREATE INDEX ix_history_project_date_range ON allocation_history(project_id, recorded_at) 
WHERE recorded_at IS NOT NULL;
