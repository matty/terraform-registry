ALTER TABLE vcs_sources
    ADD COLUMN IF NOT EXISTS tag_pattern TEXT NOT NULL DEFAULT 'v*',
    ADD COLUMN IF NOT EXISTS last_published_version TEXT NULL,
    ADD COLUMN IF NOT EXISTS last_sync_status TEXT NOT NULL DEFAULT 'never',
    ADD COLUMN IF NOT EXISTS last_sync_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS last_sync_error TEXT NULL;

CREATE INDEX IF NOT EXISTS idx_vcs_sources_module_lookup
    ON vcs_sources(namespace, name, provider, is_active);
