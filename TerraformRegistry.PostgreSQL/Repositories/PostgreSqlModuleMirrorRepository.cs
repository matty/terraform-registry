using Npgsql;
using NpgsqlTypes;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.PostgreSQL.Repositories;

public sealed class PostgreSqlModuleMirrorRepository(string connectionString) : IModuleMirrorRepository
{
    public async Task<MirrorModuleVersions?> GetModuleVersionsAsync(
        string hostname,
        string moduleNamespace,
        string name,
        string provider)
    {
        const string sql = @"
            SELECT id, hostname, namespace, name, provider, versions_json::text, etag, state, last_error, last_sync_at, created_at, updated_at
            FROM mirror_module_versions
            WHERE hostname = @hostname AND namespace = @namespace AND name = @name AND provider = @provider";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@hostname", hostname);
        command.Parameters.AddWithValue("@namespace", moduleNamespace);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@provider", provider);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapModuleVersions(reader) : null;
    }

    public async Task UpsertModuleVersionsAsync(MirrorModuleVersions moduleVersions)
    {
        const string sql = @"
            INSERT INTO mirror_module_versions (
                id, hostname, namespace, name, provider, versions_json, etag, state, last_error, last_sync_at, created_at, updated_at)
            VALUES (
                @id, @hostname, @namespace, @name, @provider, @versionsJson, @etag, @state, @lastError, @lastSyncAt, @createdAt, @updatedAt)
            ON CONFLICT(hostname, namespace, name, provider) DO UPDATE SET
                versions_json = EXCLUDED.versions_json,
                etag = EXCLUDED.etag,
                state = EXCLUDED.state,
                last_error = EXCLUDED.last_error,
                last_sync_at = EXCLUDED.last_sync_at,
                updated_at = EXCLUDED.updated_at";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", moduleVersions.Id);
        command.Parameters.AddWithValue("@hostname", moduleVersions.Hostname);
        command.Parameters.AddWithValue("@namespace", moduleVersions.Namespace);
        command.Parameters.AddWithValue("@name", moduleVersions.Name);
        command.Parameters.AddWithValue("@provider", moduleVersions.Provider);
        AddJsonb(command, "versionsJson", moduleVersions.VersionsJson);
        command.Parameters.AddWithValue("@etag", DbValue(moduleVersions.ETag));
        command.Parameters.AddWithValue("@state", moduleVersions.State);
        command.Parameters.AddWithValue("@lastError", DbValue(moduleVersions.LastError));
        command.Parameters.AddWithValue("@lastSyncAt", DbValue(moduleVersions.LastSyncAt));
        command.Parameters.AddWithValue("@createdAt", moduleVersions.CreatedAt.ToUniversalTime());
        command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<MirrorModulePackage>> ListModulePackagesAsync(
        string? q,
        string? state,
        int limit,
        int offset)
    {
        var packages = new List<MirrorModulePackage>();
        var parameters = new List<NpgsqlParameter>();
        var sql = @"
            SELECT id, hostname, namespace, name, provider, version, download_url, source, package_storage_path,
                   size_bytes, cache_size_bytes, metadata_json::text, state, last_error, http_status_code, last_sync_at, created_at, updated_at
            FROM mirror_module_packages
            WHERE TRUE";

        if (!string.IsNullOrWhiteSpace(q))
        {
            sql += @" AND (
                hostname ILIKE @q OR namespace ILIKE @q OR name ILIKE @q OR provider ILIKE @q OR version ILIKE @q)";
            parameters.Add(new NpgsqlParameter("@q", $"%{q.Trim()}%"));
        }

        if (!string.IsNullOrWhiteSpace(state))
        {
            sql += " AND state = @state";
            parameters.Add(new NpgsqlParameter("@state", state.Trim()));
        }

        sql += " ORDER BY hostname, namespace, name, provider, version LIMIT @limit OFFSET @offset";
        parameters.Add(new NpgsqlParameter("@limit", Math.Clamp(limit, 1, 1000)));
        parameters.Add(new NpgsqlParameter("@offset", Math.Max(0, offset)));

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters.ToArray());

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            packages.Add(MapModulePackage(reader));
        }

        return packages;
    }

    public async Task<MirrorModulePackage?> GetModulePackageAsync(
        string hostname,
        string moduleNamespace,
        string name,
        string provider,
        string version)
    {
        const string sql = @"
            SELECT id, hostname, namespace, name, provider, version, download_url, source, package_storage_path,
                   size_bytes, cache_size_bytes, metadata_json::text, state, last_error, http_status_code, last_sync_at, created_at, updated_at
            FROM mirror_module_packages
            WHERE hostname = @hostname AND namespace = @namespace AND name = @name
              AND provider = @provider AND version = @version";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@hostname", hostname);
        command.Parameters.AddWithValue("@namespace", moduleNamespace);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@provider", provider);
        command.Parameters.AddWithValue("@version", version);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapModulePackage(reader) : null;
    }

    public async Task UpsertModulePackageAsync(MirrorModulePackage package)
    {
        const string sql = @"
            INSERT INTO mirror_module_packages (
                id, hostname, namespace, name, provider, version, download_url, source, package_storage_path,
                size_bytes, cache_size_bytes, metadata_json, state, last_error, http_status_code, last_sync_at, created_at, updated_at)
            VALUES (
                @id, @hostname, @namespace, @name, @provider, @version, @downloadUrl, @source, @packageStoragePath,
                @sizeBytes, @cacheSizeBytes, @metadataJson, @state, @lastError, @httpStatusCode, @lastSyncAt, @createdAt, @updatedAt)
            ON CONFLICT(hostname, namespace, name, provider, version) DO UPDATE SET
                download_url = EXCLUDED.download_url,
                source = EXCLUDED.source,
                package_storage_path = EXCLUDED.package_storage_path,
                size_bytes = EXCLUDED.size_bytes,
                cache_size_bytes = EXCLUDED.cache_size_bytes,
                metadata_json = EXCLUDED.metadata_json,
                state = EXCLUDED.state,
                last_error = EXCLUDED.last_error,
                http_status_code = EXCLUDED.http_status_code,
                last_sync_at = EXCLUDED.last_sync_at,
                updated_at = EXCLUDED.updated_at";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        AddPackageParameters(command, package, DateTime.UtcNow);

        await command.ExecuteNonQueryAsync();
    }

    public async Task MarkModulePackageFailedAsync(
        string hostname,
        string moduleNamespace,
        string name,
        string provider,
        string version,
        string errorMessage,
        int? httpStatusCode = null)
    {
        const string sql = @"
            UPDATE mirror_module_packages
            SET state = 'failed',
                last_error = @error,
                http_status_code = @httpStatusCode,
                updated_at = @updatedAt
            WHERE hostname = @hostname AND namespace = @namespace AND name = @name
              AND provider = @provider AND version = @version";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@hostname", hostname);
        command.Parameters.AddWithValue("@namespace", moduleNamespace);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@provider", provider);
        command.Parameters.AddWithValue("@version", version);
        command.Parameters.AddWithValue("@error", errorMessage);
        command.Parameters.AddWithValue("@httpStatusCode", DbValue(httpStatusCode));
        command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow);

        await command.ExecuteNonQueryAsync();
    }

    private static void AddPackageParameters(NpgsqlCommand command, MirrorModulePackage package, DateTime updatedAt)
    {
        command.Parameters.AddWithValue("@id", package.Id);
        command.Parameters.AddWithValue("@hostname", package.Hostname);
        command.Parameters.AddWithValue("@namespace", package.Namespace);
        command.Parameters.AddWithValue("@name", package.Name);
        command.Parameters.AddWithValue("@provider", package.Provider);
        command.Parameters.AddWithValue("@version", package.Version);
        command.Parameters.AddWithValue("@downloadUrl", package.DownloadUrl);
        command.Parameters.AddWithValue("@source", DbValue(package.Source));
        command.Parameters.AddWithValue("@packageStoragePath", DbValue(package.PackageStoragePath));
        command.Parameters.AddWithValue("@sizeBytes", DbValue(package.SizeBytes));
        command.Parameters.AddWithValue("@cacheSizeBytes", DbValue(package.CacheSizeBytes));
        AddJsonb(command, "metadataJson", package.MetadataJson);
        command.Parameters.AddWithValue("@state", package.State);
        command.Parameters.AddWithValue("@lastError", DbValue(package.LastError));
        command.Parameters.AddWithValue("@httpStatusCode", DbValue(package.HttpStatusCode));
        command.Parameters.AddWithValue("@lastSyncAt", DbValue(package.LastSyncAt));
        command.Parameters.AddWithValue("@createdAt", package.CreatedAt.ToUniversalTime());
        command.Parameters.AddWithValue("@updatedAt", updatedAt);
    }

    private static MirrorModuleVersions MapModuleVersions(NpgsqlDataReader reader)
    {
        return new MirrorModuleVersions
        {
            Id = reader.GetGuid(0),
            Hostname = reader.GetString(1),
            Namespace = reader.GetString(2),
            Name = reader.GetString(3),
            Provider = reader.GetString(4),
            VersionsJson = reader.GetString(5),
            ETag = ReadString(reader, 6),
            State = reader.GetString(7),
            LastError = ReadString(reader, 8),
            LastSyncAt = ReadDateTime(reader, 9),
            CreatedAt = reader.GetDateTime(10),
            UpdatedAt = reader.GetDateTime(11)
        };
    }

    private static MirrorModulePackage MapModulePackage(NpgsqlDataReader reader)
    {
        return new MirrorModulePackage
        {
            Id = reader.GetGuid(0),
            Hostname = reader.GetString(1),
            Namespace = reader.GetString(2),
            Name = reader.GetString(3),
            Provider = reader.GetString(4),
            Version = reader.GetString(5),
            DownloadUrl = reader.GetString(6),
            Source = ReadString(reader, 7),
            PackageStoragePath = ReadString(reader, 8),
            SizeBytes = reader.IsDBNull(9) ? null : reader.GetInt64(9),
            CacheSizeBytes = reader.IsDBNull(10) ? null : reader.GetInt64(10),
            MetadataJson = ReadString(reader, 11),
            State = reader.GetString(12),
            LastError = ReadString(reader, 13),
            HttpStatusCode = reader.IsDBNull(14) ? null : reader.GetInt32(14),
            LastSyncAt = ReadDateTime(reader, 15),
            CreatedAt = reader.GetDateTime(16),
            UpdatedAt = reader.GetDateTime(17)
        };
    }

    private static void AddJsonb(NpgsqlCommand command, string name, string? value)
    {
        command.Parameters.Add(new NpgsqlParameter($"@{name}", NpgsqlDbType.Jsonb)
        {
            Value = (object?)value ?? DBNull.Value
        });
    }

    private static object DbValue(string? value) => (object?)value ?? DBNull.Value;

    private static object DbValue(long? value) => value.HasValue ? value.Value : DBNull.Value;

    private static object DbValue(int? value) => value.HasValue ? value.Value : DBNull.Value;

    private static object DbValue(DateTime? value) => value.HasValue ? value.Value.ToUniversalTime() : DBNull.Value;

    private static string? ReadString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static DateTime? ReadDateTime(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
}
