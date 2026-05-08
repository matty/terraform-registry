CREATE TABLE IF NOT EXISTS module_llm_contexts (
    module_id INTEGER PRIMARY KEY REFERENCES modules(id) ON DELETE CASCADE,
    schema_version TEXT NOT NULL,
    generated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    document_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    source_checksum TEXT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_module_llm_contexts_updated_at ON module_llm_contexts(updated_at);
