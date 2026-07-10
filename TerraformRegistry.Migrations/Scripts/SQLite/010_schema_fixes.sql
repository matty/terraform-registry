-- ============================================================
-- Fix 1: Recreate modules and its children (description nullable)
-- ============================================================
--
-- SQLite does not allow PRAGMA foreign_keys to be changed inside DbUp's
-- transaction. Rebuild the dependent table before replacing its parent so
-- migrations are safe with foreign-key enforcement enabled.
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
    deleted_at TEXT NULL,
    UNIQUE(namespace, name, provider, version)
);
INSERT INTO modules_new (id, namespace, name, provider, version, description, storage_path, published_at, dependencies, deleted_at)
    SELECT id, namespace, name, provider, version, description, storage_path, published_at, dependencies, deleted_at FROM modules;

CREATE TABLE module_downloads_new (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    module_id INTEGER REFERENCES modules_new(id) ON DELETE CASCADE,
    namespace TEXT NOT NULL,
    name TEXT NOT NULL,
    provider TEXT NOT NULL,
    version TEXT NOT NULL,
    download_time TEXT NOT NULL DEFAULT (datetime('now')),
    client_ip TEXT,
    user_agent TEXT
);
INSERT INTO module_downloads_new (id, module_id, namespace, name, provider, version, download_time, client_ip, user_agent)
    SELECT id, module_id, namespace, name, provider, version, download_time, client_ip, user_agent FROM module_downloads;
DROP TABLE module_downloads;
DROP TABLE modules;
ALTER TABLE modules_new RENAME TO modules;
ALTER TABLE module_downloads_new RENAME TO module_downloads;
CREATE INDEX IF NOT EXISTS idx_modules_lookup ON modules(namespace, name, provider);
CREATE INDEX IF NOT EXISTS idx_modules_deleted_at ON modules(deleted_at);
CREATE INDEX IF NOT EXISTS idx_module_downloads_time ON module_downloads(download_time);

-- ============================================================
-- Fix 2: Add UNIQUE provider+provider_id without rebuilding users.
-- Users are referenced by api_keys, webhooks, vcs_sources and user_roles.
-- ============================================================
CREATE UNIQUE INDEX IF NOT EXISTS idx_users_provider_provider_id ON users(provider, provider_id);

-- ============================================================
-- Fix 3: Recreate api_keys table (add ON DELETE CASCADE)
-- ============================================================
CREATE TABLE api_keys_new (
    id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    description TEXT NOT NULL,
    token_hash TEXT NOT NULL,
    prefix TEXT NOT NULL,
    is_shared INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    expires_at TEXT,
    last_used_at TEXT
);
INSERT INTO api_keys_new (id, user_id, description, token_hash, prefix, is_shared, created_at, expires_at, last_used_at)
    SELECT id, user_id, description, token_hash, prefix, is_shared, created_at, expires_at, last_used_at FROM api_keys;
DROP TABLE api_keys;
ALTER TABLE api_keys_new RENAME TO api_keys;
CREATE INDEX IF NOT EXISTS idx_api_keys_prefix ON api_keys(prefix);
CREATE INDEX IF NOT EXISTS idx_api_keys_user_id ON api_keys(user_id);
CREATE INDEX IF NOT EXISTS idx_api_keys_is_shared ON api_keys(is_shared);

-- ============================================================
-- Fix 4: Add missing indexes on existing tables
-- ============================================================
CREATE INDEX IF NOT EXISTS idx_webhooks_user_id ON webhooks(user_id);
CREATE INDEX IF NOT EXISTS idx_vcs_sources_user ON vcs_sources(user_id);
CREATE INDEX IF NOT EXISTS idx_vcs_sources_connection ON vcs_sources(connection_id);
CREATE INDEX IF NOT EXISTS idx_module_downloads_namespace ON module_downloads(namespace);
CREATE INDEX IF NOT EXISTS idx_module_downloads_name ON module_downloads(name);
CREATE INDEX IF NOT EXISTS idx_module_downloads_provider ON module_downloads(provider);
