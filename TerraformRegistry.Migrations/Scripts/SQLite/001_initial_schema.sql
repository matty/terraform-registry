CREATE TABLE IF NOT EXISTS modules (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    namespace TEXT NOT NULL,
    name TEXT NOT NULL,
    provider TEXT NOT NULL,
    version TEXT NOT NULL,
    description TEXT NOT NULL,
    storage_path TEXT NOT NULL,
    published_at TEXT NOT NULL,
    dependencies TEXT NOT NULL,
    deleted_at TEXT NULL,
    UNIQUE(namespace, name, provider, version)
);

CREATE INDEX IF NOT EXISTS idx_modules_lookup ON modules(namespace, name, provider);
CREATE INDEX IF NOT EXISTS idx_modules_deleted_at ON modules(deleted_at);
