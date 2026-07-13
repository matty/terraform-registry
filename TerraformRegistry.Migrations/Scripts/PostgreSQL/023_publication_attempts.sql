CREATE TABLE module_publication_attempts (
    id UUID PRIMARY KEY,
    namespace VARCHAR(255) NOT NULL,
    name VARCHAR(255) NOT NULL,
    provider VARCHAR(255) NOT NULL,
    version VARCHAR(255) NOT NULL,
    state VARCHAR(32) NOT NULL,
    staging_key TEXT NOT NULL,
    expected_revision TEXT,
    committed_revision TEXT,
    error TEXT,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    completed_at TIMESTAMP WITH TIME ZONE
);

CREATE INDEX idx_module_publication_attempts_coordinate
    ON module_publication_attempts(namespace, name, provider, version, created_at DESC);

CREATE TABLE module_extraction_jobs (
    id UUID PRIMARY KEY,
    publication_attempt_id UUID NOT NULL UNIQUE REFERENCES module_publication_attempts(id) ON DELETE CASCADE,
    namespace VARCHAR(255) NOT NULL,
    name VARCHAR(255) NOT NULL,
    provider VARCHAR(255) NOT NULL,
    version VARCHAR(255) NOT NULL,
    state VARCHAR(32) NOT NULL,
    owner_id VARCHAR(255),
    lease_expires_at TIMESTAMP WITH TIME ZONE,
    attempt_count INTEGER NOT NULL DEFAULT 0,
    last_error TEXT,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    completed_at TIMESTAMP WITH TIME ZONE
);

CREATE INDEX idx_module_extraction_jobs_claim
    ON module_extraction_jobs(state, lease_expires_at, created_at);
