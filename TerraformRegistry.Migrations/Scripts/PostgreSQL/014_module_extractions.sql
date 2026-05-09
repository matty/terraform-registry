ALTER TABLE modules ADD COLUMN IF NOT EXISTS metadata JSONB NOT NULL DEFAULT '{}'::jsonb;
CREATE INDEX IF NOT EXISTS idx_module_metadata ON modules USING GIN (metadata);

CREATE TABLE IF NOT EXISTS module_extractions (
    module_id INTEGER PRIMARY KEY REFERENCES modules(id) ON DELETE CASCADE,
    document_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    source_checksum TEXT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_module_extractions_updated_at ON module_extractions(updated_at);
