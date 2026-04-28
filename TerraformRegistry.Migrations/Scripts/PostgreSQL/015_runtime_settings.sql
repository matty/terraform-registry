CREATE TABLE IF NOT EXISTS runtime_settings (
    key TEXT PRIMARY KEY,
    value_json JSONB NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_by TEXT NULL
);

CREATE INDEX IF NOT EXISTS idx_runtime_settings_updated_at ON runtime_settings(updated_at);
