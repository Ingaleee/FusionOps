CREATE INDEX IF NOT EXISTS ix_history_project_rec ON allocation_history(project_id, recorded_at DESC);
CREATE INDEX IF NOT EXISTS ix_history_recorded ON allocation_history(recorded_at DESC);
