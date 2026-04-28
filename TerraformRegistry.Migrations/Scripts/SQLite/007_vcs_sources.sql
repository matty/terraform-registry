CREATE TABLE IF NOT EXISTS vcs_sources (
    id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    namespace TEXT NOT NULL,
    name TEXT NOT NULL,
    provider TEXT NOT NULL,
    repo_owner TEXT NOT NULL,
    repo_name TEXT NOT NULL,
    connection_id TEXT NOT NULL REFERENCES vcs_connections(id) ON DELETE CASCADE,
    is_active INTEGER NOT NULL DEFAULT 1,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_vcs_sources_module ON vcs_sources(namespace, name, provider);
CREATE INDEX IF NOT EXISTS idx_vcs_sources_repo ON vcs_sources(repo_owner, repo_name);
