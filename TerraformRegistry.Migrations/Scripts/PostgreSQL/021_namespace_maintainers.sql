CREATE TABLE namespace_maintainers (
    namespace TEXT PRIMARY KEY,
    user_id TEXT NOT NULL REFERENCES users(id),
    assigned_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_namespace_maintainers_user_id ON namespace_maintainers(user_id);
