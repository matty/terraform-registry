ALTER TABLE mirror_provider_packages ADD COLUMN IF NOT EXISTS cache_size_bytes BIGINT NULL;
ALTER TABLE mirror_module_packages ADD COLUMN IF NOT EXISTS cache_size_bytes BIGINT NULL;
