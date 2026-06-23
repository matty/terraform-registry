CREATE TABLE IF NOT EXISTS mirror_provider_indexes (
    id TEXT PRIMARY KEY,
    hostname TEXT NOT NULL,
    namespace TEXT NOT NULL,
    type TEXT NOT NULL,
    versions_json TEXT NOT NULL,
    etag TEXT NULL,
    state TEXT NOT NULL,
    last_error TEXT NULL,
    last_sync_at TEXT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_mirror_provider_indexes_coordinate
    ON mirror_provider_indexes(hostname, namespace, type);

CREATE TABLE IF NOT EXISTS mirror_provider_packages (
    id TEXT PRIMARY KEY,
    hostname TEXT NOT NULL,
    namespace TEXT NOT NULL,
    type TEXT NOT NULL,
    version TEXT NOT NULL,
    os TEXT NOT NULL,
    arch TEXT NOT NULL,
    download_url TEXT NOT NULL,
    filename TEXT NULL,
    package_storage_path TEXT NULL,
    size_bytes INTEGER NULL,
    protocols_json TEXT NOT NULL,
    hashes_json TEXT NOT NULL,
    shasum TEXT NULL,
    signing_keys_json TEXT NULL,
    state TEXT NOT NULL,
    last_error TEXT NULL,
    http_status_code INTEGER NULL,
    last_sync_at TEXT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_mirror_provider_packages_coordinate
    ON mirror_provider_packages(hostname, namespace, type, version, os, arch);

CREATE INDEX IF NOT EXISTS idx_mirror_provider_packages_state
    ON mirror_provider_packages(state);

CREATE TABLE IF NOT EXISTS mirror_module_versions (
    id TEXT PRIMARY KEY,
    hostname TEXT NOT NULL,
    namespace TEXT NOT NULL,
    name TEXT NOT NULL,
    provider TEXT NOT NULL,
    versions_json TEXT NOT NULL,
    etag TEXT NULL,
    state TEXT NOT NULL,
    last_error TEXT NULL,
    last_sync_at TEXT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_mirror_module_versions_coordinate
    ON mirror_module_versions(hostname, namespace, name, provider);

CREATE TABLE IF NOT EXISTS mirror_module_packages (
    id TEXT PRIMARY KEY,
    hostname TEXT NOT NULL,
    namespace TEXT NOT NULL,
    name TEXT NOT NULL,
    provider TEXT NOT NULL,
    version TEXT NOT NULL,
    download_url TEXT NOT NULL,
    source TEXT NULL,
    package_storage_path TEXT NULL,
    size_bytes INTEGER NULL,
    metadata_json TEXT NULL,
    state TEXT NOT NULL,
    last_error TEXT NULL,
    http_status_code INTEGER NULL,
    last_sync_at TEXT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_mirror_module_packages_coordinate
    ON mirror_module_packages(hostname, namespace, name, provider, version);

CREATE INDEX IF NOT EXISTS idx_mirror_module_packages_state
    ON mirror_module_packages(state);

CREATE TABLE IF NOT EXISTS mirror_cache_leases (
    id TEXT PRIMARY KEY,
    lease_key TEXT NOT NULL,
    operation_type TEXT NOT NULL,
    owner_instance_id TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    heartbeat_at TEXT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_mirror_cache_leases_key
    ON mirror_cache_leases(lease_key);
