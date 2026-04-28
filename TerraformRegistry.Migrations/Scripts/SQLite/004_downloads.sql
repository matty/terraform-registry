CREATE TABLE IF NOT EXISTS module_downloads (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    module_id INTEGER REFERENCES modules(id) ON DELETE CASCADE,
    namespace TEXT NOT NULL,
    name TEXT NOT NULL,
    provider TEXT NOT NULL,
    version TEXT NOT NULL,
    download_time TEXT NOT NULL DEFAULT (datetime('now')),
    client_ip TEXT,
    user_agent TEXT
);

CREATE INDEX IF NOT EXISTS idx_module_downloads_time ON module_downloads(download_time);
