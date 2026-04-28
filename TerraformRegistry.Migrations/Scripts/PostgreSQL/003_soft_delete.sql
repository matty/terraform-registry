ALTER TABLE modules ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMP WITH TIME ZONE NULL;
CREATE INDEX IF NOT EXISTS idx_modules_deleted_at ON modules(deleted_at);
