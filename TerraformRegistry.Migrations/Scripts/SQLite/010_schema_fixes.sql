-- Disable FK checks during migration
PRAGMA foreign_keys=OFF;

-- ============================================================
-- Fix 1: Recreate modules table (description nullable)
-- ============================================================
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
DROP TABLE modules;
ALTER TABLE modules_new RENAME TO modules;
CREATE INDEX IF NOT EXISTS idx_modules_lookup ON modules(namespace, name, provider);
CREATE INDEX IF NOT EXISTS idx_modules_deleted_at ON modules(deleted_at);

-- ============================================================
-- Fix 2: Recreate users table (add UNIQUE provider+provider_id)
-- ============================================================
CREATE TABLE users_new (
    id TEXT PRIMARY KEY,
    email TEXT NOT NULL,
    provider TEXT NOT NULL,
    provider_id TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    UNIQUE(email),
    UNIQUE(provider, provider_id)
);
INSERT INTO users_new (id, email, provider, provider_id, created_at, updated_at)
    SELECT id, email, provider, provider_id, created_at, updated_at FROM users;
DROP TABLE users;
ALTER TABLE users_new RENAME TO users;

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

-- Re-enable FK checks
PRAGMA foreign_keys=ON;
