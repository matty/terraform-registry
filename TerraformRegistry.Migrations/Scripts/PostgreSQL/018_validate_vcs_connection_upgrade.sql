-- Migration 010 was previously journaled after dropping vcs_sources. DbUp will
-- not replay an edited embedded script, so do not attempt a destructive repair
-- here. Validate the journaled shape and fail closed if it is not a complete,
-- usable VCS connection upgrade.
DO $$
BEGIN
    IF to_regclass('public.vcs_connections') IS NULL
       OR to_regclass('public.vcs_sources') IS NULL
       OR NOT EXISTS (
           SELECT 1 FROM information_schema.columns
           WHERE table_schema = 'public' AND table_name = 'vcs_sources'
             AND column_name = 'connection_id' AND is_nullable = 'NO')
       OR EXISTS (
           SELECT 1 FROM information_schema.columns
           WHERE table_schema = 'public' AND table_name = 'vcs_sources'
             AND column_name IN ('pat_encrypted', 'webhook_secret'))
       OR NOT EXISTS (
           SELECT 1 FROM pg_constraint
           WHERE conname = 'vcs_sources_connection_id_fkey'
             AND conrelid::regclass::text = 'vcs_sources')
    THEN
        RAISE EXCEPTION
            'Unsafe VCS migration state: migration 010 is journaled but vcs_sources is not a complete connection-backed schema. Restore a backup or follow the migration recovery runbook; no automatic destructive repair was attempted.';
    END IF;
END $$;
