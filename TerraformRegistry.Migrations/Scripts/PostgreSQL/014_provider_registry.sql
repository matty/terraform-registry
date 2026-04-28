CREATE TABLE IF NOT EXISTS providers (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    namespace VARCHAR(255) NOT NULL,
    type VARCHAR(255) NOT NULL,
    display_name VARCHAR(255),
    description TEXT,
    source_repository_url TEXT,
    created_by TEXT,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    deleted_at TIMESTAMP WITH TIME ZONE NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_providers_namespace_type
    ON providers(namespace, type)
    WHERE deleted_at IS NULL;

CREATE TABLE IF NOT EXISTS provider_gpg_keys (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    namespace VARCHAR(255) NOT NULL,
    key_id VARCHAR(64) NOT NULL,
    ascii_armor TEXT NOT NULL,
    trust_signature TEXT,
    source TEXT,
    source_url TEXT,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    revoked_at TIMESTAMP WITH TIME ZONE NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_provider_gpg_keys_namespace_key
    ON provider_gpg_keys(namespace, key_id)
    WHERE revoked_at IS NULL;

CREATE TABLE IF NOT EXISTS provider_versions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_id UUID NOT NULL REFERENCES providers(id) ON DELETE CASCADE,
    version VARCHAR(50) NOT NULL,
    protocols JSONB NOT NULL DEFAULT '[]',
    key_id VARCHAR(64) NOT NULL,
    shasums_storage_path TEXT,
    shasums_signature_storage_path TEXT,
    published_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    deleted_at TIMESTAMP WITH TIME ZONE NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_provider_versions_provider_version
    ON provider_versions(provider_id, version)
    WHERE deleted_at IS NULL;

CREATE TABLE IF NOT EXISTS provider_platforms (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_version_id UUID NOT NULL REFERENCES provider_versions(id) ON DELETE CASCADE,
    os VARCHAR(64) NOT NULL,
    arch VARCHAR(64) NOT NULL,
    filename TEXT NOT NULL,
    shasum VARCHAR(64) NOT NULL,
    package_storage_path TEXT,
    size_bytes BIGINT NOT NULL DEFAULT 0,
    uploaded_at TIMESTAMP WITH TIME ZONE NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_provider_platforms_version_platform
    ON provider_platforms(provider_version_id, os, arch);

CREATE TABLE IF NOT EXISTS provider_downloads (
    id SERIAL PRIMARY KEY,
    provider_id UUID REFERENCES providers(id) ON DELETE SET NULL,
    namespace VARCHAR(255) NOT NULL,
    type VARCHAR(255) NOT NULL,
    version VARCHAR(50) NOT NULL,
    os VARCHAR(64) NOT NULL,
    arch VARCHAR(64) NOT NULL,
    download_time TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    client_ip VARCHAR(50),
    user_agent TEXT
);

CREATE INDEX IF NOT EXISTS idx_provider_downloads_provider_id ON provider_downloads(provider_id);
CREATE INDEX IF NOT EXISTS idx_provider_downloads_namespace ON provider_downloads(namespace);
CREATE INDEX IF NOT EXISTS idx_provider_downloads_type ON provider_downloads(type);
CREATE INDEX IF NOT EXISTS idx_provider_downloads_time ON provider_downloads(download_time);
