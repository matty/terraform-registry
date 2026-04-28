ALTER TABLE module_downloads
    DROP CONSTRAINT IF EXISTS module_downloads_module_id_fkey;
ALTER TABLE module_downloads
    ADD CONSTRAINT module_downloads_module_id_fkey
    FOREIGN KEY (module_id) REFERENCES modules(id) ON DELETE CASCADE;
