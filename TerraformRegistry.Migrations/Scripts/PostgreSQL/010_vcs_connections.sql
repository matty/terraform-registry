CREATE TABLE IF NOT EXISTS vcs_connections (
    id UUID PRIMARY KEY,
    label TEXT NOT NULL,
    provider TEXT NOT NULL DEFAULT 'github',
    pat_encrypted TEXT,
    default_org TEXT,
    webhook_secret TEXT NOT NULL,
    created_by TEXT,
    is_active BOOLEAN NOT NULL DEFAULT true,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_vcs_connections_active ON vcs_connections(is_active);

ALTER TABLE vcs_sources ADD COLUMN IF NOT EXISTS connection_id UUID;

INSERT INTO vcs_connections (
    id, label, provider, pat_encrypted, default_org, webhook_secret,
    created_by, is_active, created_at, updated_at
)
SELECT id, format('Migrated %s/%s', namespace, name), 'github', pat_encrypted,
       repo_owner, webhook_secret, user_id, is_active, created_at, updated_at
FROM vcs_sources
ON CONFLICT (id) DO NOTHING;

UPDATE vcs_sources SET connection_id = id WHERE connection_id IS NULL;
ALTER TABLE vcs_sources ALTER COLUMN connection_id SET NOT NULL;
ALTER TABLE vcs_sources
    ADD CONSTRAINT vcs_sources_connection_id_fkey
    FOREIGN KEY (connection_id) REFERENCES vcs_connections(id) ON DELETE CASCADE;
ALTER TABLE vcs_sources
    ADD CONSTRAINT vcs_sources_user_id_fkey
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE;
ALTER TABLE vcs_sources DROP COLUMN pat_encrypted;
ALTER TABLE vcs_sources DROP COLUMN webhook_secret;
CREATE INDEX IF NOT EXISTS idx_vcs_sources_connection ON vcs_sources(connection_id);
