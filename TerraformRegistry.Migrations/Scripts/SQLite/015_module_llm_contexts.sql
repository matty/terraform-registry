CREATE TABLE IF NOT EXISTS module_llm_contexts (
    module_id INTEGER PRIMARY KEY,
    schema_version TEXT NOT NULL,
    generated_at TEXT NOT NULL,
    document_json TEXT NOT NULL,
    source_checksum TEXT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    FOREIGN KEY(module_id) REFERENCES modules(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_module_llm_contexts_updated_at ON module_llm_contexts(updated_at);
