namespace TerraformRegistry.PostgreSQL.Migrations;

using Npgsql;

/// <summary>
/// Combined initial database schema migration (v1.2.0)
/// Creates the core module storage tables, adds metadata, and download tracking.
/// </summary>
public class Migration_1_0_0 : IDatabaseMigration // Keep class name for simplicity, but update version/desc
{
    /// <summary>
    /// Gets the migration version in SemVer format
    /// </summary>
    public string Version => "1.2.0"; // Updated version

    /// <summary>
    /// Gets a description of what this migration does
    /// </summary>
    public string Description => "Initial schema, metadata column, and download tracking"; // Updated description

    /// <summary>
    /// Applies the migration to the database
    /// </summary>
    public async Task ApplyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        // Combined SQL from migrations 1.0.0, 1.1.0, and 1.2.0
        var combinedSql = @"
            -- Migration 1.0.0: Initial schema creation
            CREATE TABLE IF NOT EXISTS modules (
                id SERIAL PRIMARY KEY,
                namespace VARCHAR(255) NOT NULL,
                name VARCHAR(255) NOT NULL,
                provider VARCHAR(255) NOT NULL,
                version VARCHAR(50) NOT NULL,
                description TEXT,
                storage_path TEXT NOT NULL,
                published_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                dependencies JSONB NOT NULL DEFAULT '[]',
                CONSTRAINT module_unique_constraint UNIQUE (namespace, name, provider, version)
            );
            
            -- Create indexes for faster searches (1.0.0)
            CREATE INDEX IF NOT EXISTS idx_module_namespace ON modules(namespace);
            CREATE INDEX IF NOT EXISTS idx_module_name ON modules(name);
            CREATE INDEX IF NOT EXISTS idx_module_provider ON modules(provider);
            CREATE INDEX IF NOT EXISTS idx_module_version ON modules(version);

            -- Migration 1.1.0: Add metadata column
            -- Add a new column for additional metadata
            ALTER TABLE modules ADD COLUMN IF NOT EXISTS metadata JSONB NOT NULL DEFAULT '{}';
            
            -- Create an index for efficient JSON queries on metadata (1.1.0)
            CREATE INDEX IF NOT EXISTS idx_module_metadata ON modules USING GIN (metadata);

            -- Migration 1.2.0: Add module download tracking
            -- Create downloads table
            CREATE TABLE IF NOT EXISTS module_downloads (
                id SERIAL PRIMARY KEY,
                module_id INTEGER REFERENCES modules(id),
                namespace VARCHAR(255) NOT NULL,
                name VARCHAR(255) NOT NULL,
                provider VARCHAR(255) NOT NULL,
                version VARCHAR(50) NOT NULL,
                download_time TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                client_ip VARCHAR(50),
                user_agent TEXT
            );
            
            -- Create indexes for faster queries (1.2.0)
            CREATE INDEX IF NOT EXISTS idx_downloads_module_id ON module_downloads(module_id);
            CREATE INDEX IF NOT EXISTS idx_downloads_namespace ON module_downloads(namespace);
            CREATE INDEX IF NOT EXISTS idx_downloads_name ON module_downloads(name);
            CREATE INDEX IF NOT EXISTS idx_downloads_provider ON module_downloads(provider);
            CREATE INDEX IF NOT EXISTS idx_downloads_time ON module_downloads(download_time);
            
            -- Create function to record a download (1.2.0)
            CREATE OR REPLACE FUNCTION record_module_download(
                p_namespace VARCHAR(255),
                p_name VARCHAR(255),
                p_provider VARCHAR(255),
                p_version VARCHAR(50),
                p_client_ip VARCHAR(50),
                p_user_agent TEXT
            )
            RETURNS VOID AS $$
            DECLARE
                module_id INTEGER;
            BEGIN
                -- Find the module ID
                SELECT id INTO module_id FROM modules 
                WHERE namespace = p_namespace 
                AND name = p_name 
                AND provider = p_provider 
                AND version = p_version;
                
                -- Insert the download record
                INSERT INTO module_downloads (
                    module_id, 
                    namespace, 
                    name, 
                    provider, 
                    version, 
                    client_ip, 
                    user_agent
                ) VALUES (
                    module_id,
                    p_namespace,
                    p_name,
                    p_provider,
                    p_version,
                    p_client_ip,
                    p_user_agent
                );
            END;
            $$ LANGUAGE plpgsql;";

        await using var command = new NpgsqlCommand(combinedSql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }
}