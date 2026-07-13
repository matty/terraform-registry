CREATE TABLE module_publication_attempts (
    id TEXT PRIMARY KEY,
    namespace TEXT NOT NULL,
    name TEXT NOT NULL,
    provider TEXT NOT NULL,
    version TEXT NOT NULL,
    state TEXT NOT NULL,
    staging_key TEXT NOT NULL,
    expected_revision TEXT,
    committed_revision TEXT,
    error TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    completed_at TEXT
);

CREATE INDEX idx_module_publication_attempts_coordinate
    ON module_publication_attempts(namespace, name, provider, version, created_at DESC);

CREATE TABLE module_extraction_jobs (
    id TEXT PRIMARY KEY,
    publication_attempt_id TEXT NOT NULL UNIQUE REFERENCES module_publication_attempts(id) ON DELETE CASCADE,
    namespace TEXT NOT NULL,
    name TEXT NOT NULL,
    provider TEXT NOT NULL,
    version TEXT NOT NULL,
    state TEXT NOT NULL,
    owner_id TEXT,
    lease_expires_at TEXT,
    attempt_count INTEGER NOT NULL DEFAULT 0,
    last_error TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    completed_at TEXT
);

CREATE INDEX idx_module_extraction_jobs_claim
    ON module_extraction_jobs(state, lease_expires_at, created_at);
