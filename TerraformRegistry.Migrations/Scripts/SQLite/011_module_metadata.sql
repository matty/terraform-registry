PRAGMA foreign_keys=OFF;

CREATE TABLE modules_new (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    namespace TEXT NOT NULL,
    name TEXT NOT NULL,
    provider TEXT NOT NULL,
    version TEXT NOT NULL,
    description TEXT,
    storage_path TEXT NOT NULL,
    published_at TEXT NOT NULL,
    dependencies TEXT NOT NULL,
    metadata TEXT NOT NULL DEFAULT '{}',
    deleted_at TEXT NULL,
    UNIQUE(namespace, name, provider, version)
);

INSERT INTO modules_new (id, namespace, name, provider, version, description, storage_path, published_at, dependencies, metadata, deleted_at)
    SELECT id, namespace, name, provider, version, description, storage_path, published_at, dependencies, '{}', deleted_at
    FROM modules;

DROP TABLE modules;
ALTER TABLE modules_new RENAME TO modules;
CREATE INDEX IF NOT EXISTS idx_modules_lookup ON modules(namespace, name, provider);
CREATE INDEX IF NOT EXISTS idx_modules_deleted_at ON modules(deleted_at);

PRAGMA foreign_keys=ON;
