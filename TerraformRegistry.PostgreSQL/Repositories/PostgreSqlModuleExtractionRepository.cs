using System.Globalization;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.PostgreSQL.Repositories;

public sealed class PostgreSqlModuleExtractionRepository(string connectionString) : IModuleExtractionRepository
{
    public async Task<ModuleExtractionDocument?> GetModuleExtractionAsync(string moduleNamespace, string name,
        string provider, string version)
    {
        const string sql = @"
            SELECT e.document_json::text
            FROM module_extractions e
            JOIN modules m ON m.id = e.module_id
            WHERE m.namespace = @moduleNamespace
              AND m.name = @name
              AND m.provider = @provider
              AND m.version = @version";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("moduleNamespace", moduleNamespace);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@provider", provider);
        command.Parameters.AddWithValue("@version", version);

        var json = (string?)await command.ExecuteScalarAsync();
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<ModuleExtractionDocument>(json);
    }

    public async Task UpsertModuleExtractionAsync(string moduleNamespace, string name, string provider, string version,
        ModuleExtractionDocument document, string? sourceChecksum = null)
    {
        const string sql = @"
            INSERT INTO module_extractions (module_id, document_json, source_checksum, created_at, updated_at)
            SELECT id, @document, @checksum, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            FROM modules
            WHERE namespace = @moduleNamespace
              AND name = @name
              AND provider = @provider
              AND version = @version
              AND deleted_at IS NULL
            ON CONFLICT (module_id) DO UPDATE SET
                document_json = EXCLUDED.document_json,
                source_checksum = EXCLUDED.source_checksum,
                updated_at = CURRENT_TIMESTAMP";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("moduleNamespace", moduleNamespace);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@provider", provider);
        command.Parameters.AddWithValue("@version", version);
        command.Parameters.AddWithValue("@document", JsonSerializer.Serialize(document)).NpgsqlDbType = NpgsqlDbType.Jsonb;
        command.Parameters.AddWithValue("@checksum", (object?)sourceChecksum ?? DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<ModuleLlmContextDocument?> GetModuleLlmContextAsync(string moduleNamespace, string name,
        string provider, string version, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT c.document_json::text
            FROM module_llm_contexts c
            JOIN modules m ON m.id = c.module_id
            WHERE m.namespace = @moduleNamespace
              AND m.name = @name
              AND m.provider = @provider
              AND m.version = @version";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("moduleNamespace", moduleNamespace);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@provider", provider);
        command.Parameters.AddWithValue("@version", version);

        var json = (string?)await command.ExecuteScalarAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<ModuleLlmContextDocument>(json);
    }

    public async Task UpsertModuleLlmContextAsync(string moduleNamespace, string name, string provider, string version,
        ModuleLlmContextDocument document, string? sourceChecksum = null)
    {
        const string sql = @"
            INSERT INTO module_llm_contexts (module_id, schema_version, generated_at, document_json, source_checksum, created_at, updated_at)
            SELECT id, @schemaVersion, @generatedAt, @document, @checksum, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            FROM modules
            WHERE namespace = @moduleNamespace
              AND name = @name
              AND provider = @provider
              AND version = @version
              AND deleted_at IS NULL
            ON CONFLICT (module_id) DO UPDATE SET
                schema_version = EXCLUDED.schema_version,
                generated_at = EXCLUDED.generated_at,
                document_json = EXCLUDED.document_json,
                source_checksum = EXCLUDED.source_checksum,
                updated_at = CURRENT_TIMESTAMP";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("moduleNamespace", moduleNamespace);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@provider", provider);
        command.Parameters.AddWithValue("@version", version);
        command.Parameters.AddWithValue("@schemaVersion", document.SchemaVersion);
        command.Parameters.AddWithValue("@generatedAt", document.GeneratedAt);
        command.Parameters.AddWithValue("@document", JsonSerializer.Serialize(document)).NpgsqlDbType = NpgsqlDbType.Jsonb;
        command.Parameters.AddWithValue("@checksum", (object?)sourceChecksum ?? DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateModuleMetadataAsync(string moduleNamespace, string name, string provider, string version,
        Action<ModuleArtifactMetadata> mutate)
    {
        const string selectSql = @"
            SELECT metadata::text
            FROM modules
            WHERE namespace = @moduleNamespace
              AND name = @name
              AND provider = @provider
              AND version = @version
              AND deleted_at IS NULL";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var selectCommand = new NpgsqlCommand(selectSql, connection);
        selectCommand.Parameters.AddWithValue("moduleNamespace", moduleNamespace);
        selectCommand.Parameters.AddWithValue("@name", name);
        selectCommand.Parameters.AddWithValue("@provider", provider);
        selectCommand.Parameters.AddWithValue("@version", version);

        var currentJson = (string?)await selectCommand.ExecuteScalarAsync();
        var metadata = DeserializeModuleMetadata(currentJson);
        mutate(metadata);

        const string updateSql = @"
            UPDATE modules
            SET metadata = @metadata
            WHERE namespace = @moduleNamespace
              AND name = @name
              AND provider = @provider
              AND version = @version
              AND deleted_at IS NULL";

        await using var updateCommand = new NpgsqlCommand(updateSql, connection);
        updateCommand.Parameters.AddWithValue("moduleNamespace", moduleNamespace);
        updateCommand.Parameters.AddWithValue("@name", name);
        updateCommand.Parameters.AddWithValue("@provider", provider);
        updateCommand.Parameters.AddWithValue("@version", version);
        updateCommand.Parameters.AddWithValue("@metadata", JsonSerializer.Serialize(metadata)).NpgsqlDbType =
            NpgsqlDbType.Jsonb;

        await updateCommand.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<ModuleStorage>> ListModulesNeedingExtractionAsync(int limit)
    {
        var modules = new List<ModuleStorage>();

        const string sql = @"
            SELECT m.namespace, m.name, m.provider, m.version, m.description, m.storage_path, m.published_at, m.dependencies::text, m.metadata::text
            FROM modules m
            LEFT JOIN module_extractions e ON e.module_id = m.id
            WHERE m.deleted_at IS NULL AND e.module_id IS NULL
            ORDER BY m.published_at
            LIMIT @limit";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@limit", limit);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            modules.Add(MapModuleStorage(reader));
        }

        return modules;
    }

    public async Task<ModuleExtractionAdminSummary> GetModuleExtractionAdminSummaryAsync()
    {
        var summary = new ModuleExtractionAdminSummary();

        const string sql = @"
            SELECT m.metadata::text,
                   CASE WHEN e.module_id IS NULL THEN 0 ELSE 1 END,
                   CASE WHEN c.module_id IS NULL THEN 0 ELSE 1 END
            FROM modules m
            LEFT JOIN module_extractions e ON e.module_id = m.id
            LEFT JOIN module_llm_contexts c ON c.module_id = m.id
            WHERE m.deleted_at IS NULL";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            summary.Total++;
            var metadata = DeserializeModuleMetadata(reader.IsDBNull(0) ? null : reader.GetString(0));
            IncrementStatus(summary, metadata.Extraction?.Status);
            IncrementLlmStatus(summary, metadata.LlmContext?.Status);

            if (Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture) == 0)
                summary.NeverExtracted++;
            if (Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture) == 0)
                summary.LlmNeverGenerated++;
        }

        return summary;
    }

    public async Task<ModuleExtractionAdminPage> ListModuleExtractionsAdminAsync(ModuleExtractionAdminQuery query)
    {
        var items = new List<ModuleExtractionAdminListItem>();

        const string sql = @"
            SELECT m.namespace, m.name, m.provider, m.version, m.description, m.metadata::text
            FROM modules m
            WHERE m.deleted_at IS NULL
            ORDER BY m.namespace, m.name, m.provider, m.version";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(MapModuleExtractionAdminListItem(reader));
        }

        var filtered = items.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var q = query.Q.Trim();
            filtered = filtered.Where(item =>
                item.Namespace.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                item.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                item.Provider.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                item.Version.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (item.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            filtered = filtered.Where(item => string.Equals(
                item.Status,
                query.Status,
                StringComparison.OrdinalIgnoreCase));
        }

        var filteredItems = filtered.ToList();
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, 100);

        return new ModuleExtractionAdminPage
        {
            Total = filteredItems.Count,
            Items = filteredItems.Skip(offset).Take(limit).ToList()
        };
    }

    public async Task<ModuleExtractionAdminDetail?> GetModuleExtractionAdminDetailAsync(string moduleNamespace, string name,
        string provider, string version)
    {
        const string sql = @"
            SELECT m.namespace, m.name, m.provider, m.version, m.description, m.metadata::text, e.document_json::text, c.document_json::text
            FROM modules m
            LEFT JOIN module_extractions e ON e.module_id = m.id
            LEFT JOIN module_llm_contexts c ON c.module_id = m.id
            WHERE m.namespace = @moduleNamespace
              AND m.name = @name
              AND m.provider = @provider
              AND m.version = @version
              AND m.deleted_at IS NULL";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("moduleNamespace", moduleNamespace);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@provider", provider);
        command.Parameters.AddWithValue("@version", version);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        var item = MapModuleExtractionAdminListItem(reader);
        var documentJson = reader.IsDBNull(6) ? null : reader.GetString(6);
        var llmContextJson = reader.IsDBNull(7) ? null : reader.GetString(7);

        return new ModuleExtractionAdminDetail
        {
            Namespace = item.Namespace,
            Name = item.Name,
            Provider = item.Provider,
            Version = item.Version,
            Description = item.Description,
            Status = item.Status,
            LastAttemptedAt = item.LastAttemptedAt,
            LastSucceededAt = item.LastSucceededAt,
            Error = item.Error,
            LlmStatus = item.LlmStatus,
            LlmLastAttemptedAt = item.LlmLastAttemptedAt,
            LlmLastSucceededAt = item.LlmLastSucceededAt,
            LlmError = item.LlmError,
            Documentation = item.Documentation,
            Document = string.IsNullOrWhiteSpace(documentJson)
                ? null
                : JsonSerializer.Deserialize<ModuleExtractionDocument>(documentJson),
            LlmContext = string.IsNullOrWhiteSpace(llmContextJson)
                ? null
                : JsonSerializer.Deserialize<ModuleLlmContextDocument>(llmContextJson)
        };
    }

    public async Task<IReadOnlyList<ModuleStorage>> ListModulesForExtractionBackfillAsync(int limit)
    {
        var modules = new List<ModuleStorage>();

        const string sql = @"
            SELECT m.namespace, m.name, m.provider, m.version, m.description, m.storage_path, m.published_at, m.dependencies::text, m.metadata::text
            FROM modules m
            LEFT JOIN module_extractions e ON e.module_id = m.id
            WHERE m.deleted_at IS NULL
              AND (
                e.module_id IS NULL
                OR COALESCE(m.metadata->'Extraction'->>'Status', 'pending') = 'failed'
              )
            ORDER BY m.published_at
            LIMIT @limit";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 100));

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            modules.Add(MapModuleStorage(reader));
        }

        return modules;
    }
    private static ModuleArtifactMetadata DeserializeModuleMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ModuleArtifactMetadata();

        return JsonSerializer.Deserialize<ModuleArtifactMetadata>(json) ?? new ModuleArtifactMetadata();
    }

    private static ModuleExtractionAdminListItem MapModuleExtractionAdminListItem(NpgsqlDataReader reader)
    {
        var metadata = DeserializeModuleMetadata(reader.IsDBNull(5) ? null : reader.GetString(5));
        return new ModuleExtractionAdminListItem
        {
            Namespace = reader.GetString(0),
            Name = reader.GetString(1),
            Provider = reader.GetString(2),
            Version = reader.GetString(3),
            Description = reader.IsDBNull(4) ? null : reader.GetString(4),
            Status = metadata.Extraction?.Status ?? "pending",
            LastAttemptedAt = metadata.Extraction?.LastAttemptedAt,
            LastSucceededAt = metadata.Extraction?.LastSucceededAt,
            Error = metadata.Extraction?.Error,
            LlmStatus = metadata.LlmContext?.Status ?? "pending",
            LlmLastAttemptedAt = metadata.LlmContext?.LastAttemptedAt,
            LlmLastSucceededAt = metadata.LlmContext?.LastSucceededAt,
            LlmError = metadata.LlmContext?.Error,
            Documentation = metadata.Documentation
        };
    }

    private static void IncrementStatus(ModuleExtractionAdminSummary summary, string? status)
    {
        switch (status)
        {
            case "succeeded":
                summary.Succeeded++;
                break;
            case "failed":
                summary.Failed++;
                break;
            case "processing":
                summary.Processing++;
                break;
            default:
                summary.Pending++;
                break;
        }
    }

    private static void IncrementLlmStatus(ModuleExtractionAdminSummary summary, string? status)
    {
        switch (status)
        {
            case "succeeded":
                summary.LlmSucceeded++;
                break;
            case "failed":
                summary.LlmFailed++;
                break;
            case "processing":
                summary.LlmProcessing++;
                break;
            default:
                summary.LlmPending++;
                break;
        }
    }
    private static ModuleStorage MapModuleStorage(NpgsqlDataReader reader)
    {
        var dependenciesJson = reader.GetString(7);
        var dependencies = JsonSerializer.Deserialize<List<string>>(dependenciesJson) ?? new List<string>();

        return new ModuleStorage
        {
            Namespace = reader.GetString(0),
            Name = reader.GetString(1),
            Provider = reader.GetString(2),
            Version = reader.GetString(3),
            Description = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
            FilePath = reader.GetString(5),
            PublishedAt = reader.GetDateTime(6),
            Dependencies = dependencies,
            Metadata = DeserializeModuleMetadata(reader.GetString(8))
        };
    }
}
