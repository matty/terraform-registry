CREATE TABLE IF NOT EXISTS mirror_provider_indexes (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    hostname TEXT NOT NULL,
    namespace TEXT NOT NULL,
    type TEXT NOT NULL,
    versions_json JSONB NOT NULL,
    etag TEXT NULL,
    state TEXT NOT NULL,
    last_error TEXT NULL,
    last_sync_at TIMESTAMP WITH TIME ZONE NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_mirror_provider_indexes_coordinate
    ON mirror_provider_indexes(hostname, namespace, type);

CREATE TABLE IF NOT EXISTS mirror_provider_packages (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    hostname TEXT NOT NULL,
    namespace TEXT NOT NULL,
    type TEXT NOT NULL,
    version TEXT NOT NULL,
    os TEXT NOT NULL,
    arch TEXT NOT NULL,
    download_url TEXT NOT NULL,
    filename TEXT NULL,
    package_storage_path TEXT NULL,
    size_bytes BIGINT NULL,
    protocols_json JSONB NOT NULL,
    hashes_json JSONB NOT NULL,
    shasum TEXT NULL,
    signing_keys_json JSONB NULL,
    state TEXT NOT NULL,
    last_error TEXT NULL,
    http_status_code INTEGER NULL,
    last_sync_at TIMESTAMP WITH TIME ZONE NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_mirror_provider_packages_coordinate
    ON mirror_provider_packages(hostname, namespace, type, version, os, arch);

CREATE INDEX IF NOT EXISTS idx_mirror_provider_packages_state
    ON mirror_provider_packages(state);

CREATE TABLE IF NOT EXISTS mirror_module_versions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    hostname TEXT NOT NULL,
    namespace TEXT NOT NULL,
    name TEXT NOT NULL,
    provider TEXT NOT NULL,
    versions_json JSONB NOT NULL,
    etag TEXT NULL,
    state TEXT NOT NULL,
    last_error TEXT NULL,
    last_sync_at TIMESTAMP WITH TIME ZONE NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_mirror_module_versions_coordinate
    ON mirror_module_versions(hostname, namespace, name, provider);

CREATE TABLE IF NOT EXISTS mirror_module_packages (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    hostname TEXT NOT NULL,
    namespace TEXT NOT NULL,
    name TEXT NOT NULL,
    provider TEXT NOT NULL,
    version TEXT NOT NULL,
    download_url TEXT NOT NULL,
    source TEXT NULL,
    package_storage_path TEXT NULL,
    size_bytes BIGINT NULL,
    metadata_json JSONB NULL,
    state TEXT NOT NULL,
    last_error TEXT NULL,
    http_status_code INTEGER NULL,
    last_sync_at TIMESTAMP WITH TIME ZONE NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_mirror_module_packages_coordinate
    ON mirror_module_packages(hostname, namespace, name, provider, version);

CREATE INDEX IF NOT EXISTS idx_mirror_module_packages_state
    ON mirror_module_packages(state);

CREATE TABLE IF NOT EXISTS mirror_cache_leases (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    lease_key TEXT NOT NULL,
    operation_type TEXT NOT NULL,
    owner_instance_id TEXT NOT NULL,
    expires_at TIMESTAMP WITH TIME ZONE NOT NULL,
    heartbeat_at TIMESTAMP WITH TIME ZONE NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_mirror_cache_leases_key
    ON mirror_cache_leases(lease_key);
