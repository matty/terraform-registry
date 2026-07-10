CREATE TABLE IF NOT EXISTS vcs_connections (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
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

-- Do not rebuild vcs_sources here. The original migration dropped the table and
-- irreversibly lost every source, PAT, webhook secret, and timestamp. Expand the
-- existing table, copy each legacy credential into a connection, then contract
-- only the duplicated credential columns after every source has a relationship.
ALTER TABLE vcs_sources ADD COLUMN IF NOT EXISTS connection_id UUID;

DO $$
DECLARE
    source_row RECORD;
    new_connection_id UUID;
BEGIN
    FOR source_row IN
        SELECT id, user_id, namespace, name, provider, repo_owner, repo_name,
               pat_encrypted, webhook_secret, is_active, created_at, updated_at
        FROM vcs_sources
        WHERE connection_id IS NULL
    LOOP
        INSERT INTO vcs_connections (
            label, provider, pat_encrypted, webhook_secret, created_by,
            is_active, created_at, updated_at)
        VALUES (
            source_row.repo_owner || '/' || source_row.repo_name,
            source_row.provider,
            source_row.pat_encrypted,
            source_row.webhook_secret,
            source_row.user_id,
            source_row.is_active,
            source_row.created_at,
            source_row.updated_at)
        RETURNING id INTO new_connection_id;

        UPDATE vcs_sources
        SET connection_id = new_connection_id
        WHERE id = source_row.id;
    END LOOP;
END $$;

ALTER TABLE vcs_sources ALTER COLUMN connection_id SET NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'vcs_sources_connection_id_fkey'
          AND conrelid = 'vcs_sources'::regclass)
    THEN
        ALTER TABLE vcs_sources
            ADD CONSTRAINT vcs_sources_connection_id_fkey
            FOREIGN KEY (connection_id) REFERENCES vcs_connections(id) ON DELETE CASCADE;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'vcs_sources_user_id_fkey'
          AND conrelid = 'vcs_sources'::regclass)
    THEN
        ALTER TABLE vcs_sources
            ADD CONSTRAINT vcs_sources_user_id_fkey
            FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE;
    END IF;
END $$;

ALTER TABLE vcs_sources DROP COLUMN IF EXISTS pat_encrypted;
ALTER TABLE vcs_sources DROP COLUMN IF EXISTS webhook_secret;

CREATE INDEX IF NOT EXISTS idx_vcs_sources_connection ON vcs_sources(connection_id);
