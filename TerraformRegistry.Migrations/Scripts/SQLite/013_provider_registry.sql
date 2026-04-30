CREATE TABLE IF NOT EXISTS providers (
    id TEXT PRIMARY KEY,
    namespace TEXT NOT NULL,
    type TEXT NOT NULL,
    display_name TEXT,
    description TEXT,
    source_repository_url TEXT,
    created_by TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    deleted_at TEXT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_providers_namespace_type
    ON providers(namespace, type)
    WHERE deleted_at IS NULL;

CREATE TABLE IF NOT EXISTS provider_gpg_keys (
    id TEXT PRIMARY KEY,
    namespace TEXT NOT NULL,
    key_id TEXT NOT NULL,
    ascii_armor TEXT NOT NULL,
    trust_signature TEXT,
    source TEXT,
    source_url TEXT,
    created_at TEXT NOT NULL,
    revoked_at TEXT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_provider_gpg_keys_namespace_key
    ON provider_gpg_keys(namespace, key_id)
    WHERE revoked_at IS NULL;

CREATE TABLE IF NOT EXISTS provider_versions (
    id TEXT PRIMARY KEY,
    provider_id TEXT NOT NULL,
    version TEXT NOT NULL,
    protocols TEXT NOT NULL DEFAULT '[]',
    key_id TEXT NOT NULL,
    shasums_storage_path TEXT,
    shasums_signature_storage_path TEXT,
    published_at TEXT NOT NULL,
    deleted_at TEXT NULL,
    FOREIGN KEY(provider_id) REFERENCES providers(id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_provider_versions_provider_version
    ON provider_versions(provider_id, version)
    WHERE deleted_at IS NULL;

CREATE TABLE IF NOT EXISTS provider_platforms (
    id TEXT PRIMARY KEY,
    provider_version_id TEXT NOT NULL,
    os TEXT NOT NULL,
    arch TEXT NOT NULL,
    filename TEXT NOT NULL,
    shasum TEXT NOT NULL,
    package_storage_path TEXT,
    size_bytes INTEGER NOT NULL DEFAULT 0,
    uploaded_at TEXT NULL,
    FOREIGN KEY(provider_version_id) REFERENCES provider_versions(id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_provider_platforms_version_platform
    ON provider_platforms(provider_version_id, os, arch);

CREATE TABLE IF NOT EXISTS provider_downloads (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    provider_id TEXT NULL,
    namespace TEXT NOT NULL,
    type TEXT NOT NULL,
    version TEXT NOT NULL,
    os TEXT NOT NULL,
    arch TEXT NOT NULL,
    download_time TEXT NOT NULL,
    client_ip TEXT,
    user_agent TEXT,
    FOREIGN KEY(provider_id) REFERENCES providers(id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_provider_downloads_provider_id ON provider_downloads(provider_id);
CREATE INDEX IF NOT EXISTS idx_provider_downloads_namespace ON provider_downloads(namespace);
CREATE INDEX IF NOT EXISTS idx_provider_downloads_type ON provider_downloads(type);
CREATE INDEX IF NOT EXISTS idx_provider_downloads_time ON provider_downloads(download_time);
