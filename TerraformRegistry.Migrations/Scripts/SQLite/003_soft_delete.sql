-- deleted_at column is included in 001_initial_schema.sql for new databases.
-- Existing databases are bootstrapped with all scripts marked as applied,
-- so this script will never run against a database missing the column.
SELECT 1;
