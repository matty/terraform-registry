ALTER TABLE vcs_sources ADD COLUMN tag_pattern TEXT NOT NULL DEFAULT 'v*';
ALTER TABLE vcs_sources ADD COLUMN last_published_version TEXT NULL;
ALTER TABLE vcs_sources ADD COLUMN last_sync_status TEXT NOT NULL DEFAULT 'never';
ALTER TABLE vcs_sources ADD COLUMN last_sync_at TEXT NULL;
ALTER TABLE vcs_sources ADD COLUMN last_sync_error TEXT NULL;

CREATE INDEX IF NOT EXISTS idx_vcs_sources_module_lookup
    ON vcs_sources(namespace, name, provider, is_active);
