CREATE TABLE durable_outbox_events (
    id UUID PRIMARY KEY,
    kind VARCHAR(128) NOT NULL,
    idempotency_key VARCHAR(512) NOT NULL UNIQUE,
    payload_json TEXT NOT NULL,
    state VARCHAR(32) NOT NULL,
    owner_id VARCHAR(255),
    lease_expires_at TIMESTAMP WITH TIME ZONE,
    attempt_count INTEGER NOT NULL DEFAULT 0,
    last_error TEXT,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    delivered_at TIMESTAMP WITH TIME ZONE
);

CREATE INDEX idx_durable_outbox_events_claim
    ON durable_outbox_events(state, lease_expires_at, created_at);
