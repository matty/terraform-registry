CREATE TABLE IF NOT EXISTS runtime_settings (
    key TEXT PRIMARY KEY,
    value_json TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    updated_by TEXT NULL
);

CREATE INDEX IF NOT EXISTS idx_runtime_settings_updated_at ON runtime_settings(updated_at);
