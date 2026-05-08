ALTER TABLE modules ADD COLUMN metadata TEXT NOT NULL DEFAULT '{}';

CREATE TABLE IF NOT EXISTS module_extractions (
    module_id INTEGER PRIMARY KEY,
    document_json TEXT NOT NULL,
    source_checksum TEXT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    FOREIGN KEY(module_id) REFERENCES modules(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_module_extractions_updated_at ON module_extractions(updated_at);
