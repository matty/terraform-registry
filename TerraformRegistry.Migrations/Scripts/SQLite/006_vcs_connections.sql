CREATE TABLE IF NOT EXISTS vcs_connections (
    id TEXT PRIMARY KEY,
    label TEXT NOT NULL,
    provider TEXT NOT NULL DEFAULT 'github',
    pat_encrypted TEXT,
    default_org TEXT,
    webhook_secret TEXT NOT NULL,
    created_by TEXT,
    is_active INTEGER NOT NULL DEFAULT 1,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);
