using Npgsql;
using NpgsqlTypes;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.PostgreSQL.Repositories;

public sealed class PostgreSqlProviderMirrorRepository(string connectionString) : IProviderMirrorRepository
{
    public async Task<MirrorProviderIndex?> GetProviderIndexAsync(
        string hostname,
        string providerNamespace,
        string type)
    {
        const string sql = @"
            SELECT id, hostname, namespace, type, versions_json::text, etag, state, last_error, last_sync_at, created_at, updated_at
            FROM mirror_provider_indexes
            WHERE hostname = @hostname AND namespace = @namespace AND type = @type";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@hostname", hostname);
        command.Parameters.AddWithValue("@namespace", providerNamespace);
        command.Parameters.AddWithValue("@type", type);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapProviderIndex(reader) : null;
    }

    public async Task UpsertProviderIndexAsync(MirrorProviderIndex providerIndex)
    {
        const string sql = @"
            INSERT INTO mirror_provider_indexes (
                id, hostname, namespace, type, versions_json, etag, state, last_error, last_sync_at, created_at, updated_at)
            VALUES (
                @id, @hostname, @namespace, @type, @versionsJson, @etag, @state, @lastError, @lastSyncAt, @createdAt, @updatedAt)
            ON CONFLICT(hostname, namespace, type) DO UPDATE SET
                versions_json = EXCLUDED.versions_json,
                etag = EXCLUDED.etag,
                state = EXCLUDED.state,
                last_error = EXCLUDED.last_error,
                last_sync_at = EXCLUDED.last_sync_at,
                updated_at = EXCLUDED.updated_at";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", providerIndex.Id);
        command.Parameters.AddWithValue("@hostname", providerIndex.Hostname);
        command.Parameters.AddWithValue("@namespace", providerIndex.Namespace);
        command.Parameters.AddWithValue("@type", providerIndex.Type);
        AddJsonb(command, "versionsJson", providerIndex.VersionsJson);
        command.Parameters.AddWithValue("@etag", DbValue(providerIndex.ETag));
        command.Parameters.AddWithValue("@state", providerIndex.State);
        command.Parameters.AddWithValue("@lastError", DbValue(providerIndex.LastError));
        command.Parameters.AddWithValue("@lastSyncAt", DbValue(providerIndex.LastSyncAt));
        command.Parameters.AddWithValue("@createdAt", providerIndex.CreatedAt.ToUniversalTime());
        command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<MirrorProviderPackage>> ListProviderPackagesAsync(
        string? q,
        string? state,
        int limit,
        int offset)
    {
        var packages = new List<MirrorProviderPackage>();
        var parameters = new List<NpgsqlParameter>();
        var sql = @"
            SELECT id, hostname, namespace, type, version, os, arch, download_url, filename, package_storage_path,
                   size_bytes, cache_size_bytes, protocols_json::text, hashes_json::text, shasum, signing_keys_json::text, state,
                   last_error, http_status_code, last_sync_at, created_at, updated_at
            FROM mirror_provider_packages
            WHERE TRUE";

        if (!string.IsNullOrWhiteSpace(q))
        {
            sql += @" AND (
                hostname ILIKE @q OR namespace ILIKE @q OR type ILIKE @q OR version ILIKE @q OR os ILIKE @q OR arch ILIKE @q)";
            parameters.Add(new NpgsqlParameter("@q", $"%{q.Trim()}%"));
        }

        if (!string.IsNullOrWhiteSpace(state))
        {
            sql += " AND state = @state";
            parameters.Add(new NpgsqlParameter("@state", state.Trim()));
        }

        sql += " ORDER BY hostname, namespace, type, version, os, arch LIMIT @limit OFFSET @offset";
        parameters.Add(new NpgsqlParameter("@limit", Math.Clamp(limit, 1, 1000)));
        parameters.Add(new NpgsqlParameter("@offset", Math.Max(0, offset)));

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters.ToArray());

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            packages.Add(MapProviderPackage(reader));
        }

        return packages;
    }

    public async Task<MirrorProviderPackage?> GetProviderPackageAsync(
        string hostname,
        string providerNamespace,
        string type,
        string version,
        string os,
        string arch)
    {
        const string sql = @"
            SELECT id, hostname, namespace, type, version, os, arch, download_url, filename, package_storage_path,
                   size_bytes, cache_size_bytes, protocols_json::text, hashes_json::text, shasum, signing_keys_json::text, state,
                   last_error, http_status_code, last_sync_at, created_at, updated_at
            FROM mirror_provider_packages
            WHERE hostname = @hostname AND namespace = @namespace AND type = @type
              AND version = @version AND os = @os AND arch = @arch";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@hostname", hostname);
        command.Parameters.AddWithValue("@namespace", providerNamespace);
        command.Parameters.AddWithValue("@type", type);
        command.Parameters.AddWithValue("@version", version);
        command.Parameters.AddWithValue("@os", os);
        command.Parameters.AddWithValue("@arch", arch);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapProviderPackage(reader) : null;
    }

    public async Task UpsertProviderPackageAsync(MirrorProviderPackage package)
    {
        const string sql = @"
            INSERT INTO mirror_provider_packages (
                id, hostname, namespace, type, version, os, arch, download_url, filename, package_storage_path,
                size_bytes, cache_size_bytes, protocols_json, hashes_json, shasum, signing_keys_json, state, last_error,
                http_status_code, last_sync_at, created_at, updated_at)
            VALUES (
                @id, @hostname, @namespace, @type, @version, @os, @arch, @downloadUrl, @filename, @packageStoragePath,
                @sizeBytes, @cacheSizeBytes, @protocolsJson, @hashesJson, @shasum, @signingKeysJson, @state, @lastError,
                @httpStatusCode, @lastSyncAt, @createdAt, @updatedAt)
            ON CONFLICT(hostname, namespace, type, version, os, arch) DO UPDATE SET
                download_url = EXCLUDED.download_url,
                filename = EXCLUDED.filename,
                package_storage_path = EXCLUDED.package_storage_path,
                size_bytes = EXCLUDED.size_bytes,
                cache_size_bytes = EXCLUDED.cache_size_bytes,
                protocols_json = EXCLUDED.protocols_json,
                hashes_json = EXCLUDED.hashes_json,
                shasum = EXCLUDED.shasum,
                signing_keys_json = EXCLUDED.signing_keys_json,
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

    public async Task MarkProviderPackageFailedAsync(
        string hostname,
        string providerNamespace,
        string type,
        string version,
        string os,
        string arch,
        string errorMessage,
        int? httpStatusCode = null)
    {
        const string sql = @"
            INSERT INTO mirror_provider_packages (
                id, hostname, namespace, type, version, os, arch, download_url, protocols_json, hashes_json,
                state, last_error, http_status_code, created_at, updated_at)
            VALUES (
                @id, @hostname, @namespace, @type, @version, @os, @arch, @downloadUrl, '[]'::jsonb, '[]'::jsonb,
                'failed', @error, @httpStatusCode, @createdAt, @updatedAt)
            ON CONFLICT(hostname, namespace, type, version, os, arch) DO UPDATE SET
                state = 'failed',
                last_error = EXCLUDED.last_error,
                http_status_code = EXCLUDED.http_status_code,
                updated_at = EXCLUDED.updated_at";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var now = DateTime.UtcNow;
        command.Parameters.AddWithValue("@id", Guid.NewGuid());
        command.Parameters.AddWithValue("@hostname", hostname);
        command.Parameters.AddWithValue("@namespace", providerNamespace);
        command.Parameters.AddWithValue("@type", type);
        command.Parameters.AddWithValue("@version", version);
        command.Parameters.AddWithValue("@os", os);
        command.Parameters.AddWithValue("@arch", arch);
        command.Parameters.AddWithValue("@downloadUrl", DefaultProviderDownloadUrl(providerNamespace, type, version, os, arch));
        command.Parameters.AddWithValue("@error", errorMessage);
        command.Parameters.AddWithValue("@httpStatusCode", DbValue(httpStatusCode));
        command.Parameters.AddWithValue("@createdAt", now);
        command.Parameters.AddWithValue("@updatedAt", now);

        await command.ExecuteNonQueryAsync();
    }

    private static void AddPackageParameters(NpgsqlCommand command, MirrorProviderPackage package, DateTime updatedAt)
    {
        command.Parameters.AddWithValue("@id", package.Id);
        command.Parameters.AddWithValue("@hostname", package.Hostname);
        command.Parameters.AddWithValue("@namespace", package.Namespace);
        command.Parameters.AddWithValue("@type", package.Type);
        command.Parameters.AddWithValue("@version", package.Version);
        command.Parameters.AddWithValue("@os", package.Os);
        command.Parameters.AddWithValue("@arch", package.Arch);
        command.Parameters.AddWithValue("@downloadUrl", package.DownloadUrl);
        command.Parameters.AddWithValue("@filename", DbValue(package.Filename));
        command.Parameters.AddWithValue("@packageStoragePath", DbValue(package.PackageStoragePath));
        command.Parameters.AddWithValue("@sizeBytes", DbValue(package.SizeBytes));
        command.Parameters.AddWithValue("@cacheSizeBytes", DbValue(package.CacheSizeBytes));
        AddJsonb(command, "protocolsJson", package.ProtocolsJson);
        AddJsonb(command, "hashesJson", package.HashesJson);
        command.Parameters.AddWithValue("@shasum", DbValue(package.Shasum));
        AddJsonb(command, "signingKeysJson", package.SigningKeysJson);
        command.Parameters.AddWithValue("@state", package.State);
        command.Parameters.AddWithValue("@lastError", DbValue(package.LastError));
        command.Parameters.AddWithValue("@httpStatusCode", DbValue(package.HttpStatusCode));
        command.Parameters.AddWithValue("@lastSyncAt", DbValue(package.LastSyncAt));
        command.Parameters.AddWithValue("@createdAt", package.CreatedAt.ToUniversalTime());
        command.Parameters.AddWithValue("@updatedAt", updatedAt);
    }

    private static MirrorProviderIndex MapProviderIndex(NpgsqlDataReader reader)
    {
        return new MirrorProviderIndex
        {
            Id = reader.GetGuid(0),
            Hostname = reader.GetString(1),
            Namespace = reader.GetString(2),
            Type = reader.GetString(3),
            VersionsJson = reader.GetString(4),
            ETag = ReadString(reader, 5),
            State = reader.GetString(6),
            LastError = ReadString(reader, 7),
            LastSyncAt = ReadDateTime(reader, 8),
            CreatedAt = reader.GetDateTime(9),
            UpdatedAt = reader.GetDateTime(10)
        };
    }

    private static MirrorProviderPackage MapProviderPackage(NpgsqlDataReader reader)
    {
        return new MirrorProviderPackage
        {
            Id = reader.GetGuid(0),
            Hostname = reader.GetString(1),
            Namespace = reader.GetString(2),
            Type = reader.GetString(3),
            Version = reader.GetString(4),
            Os = reader.GetString(5),
            Arch = reader.GetString(6),
            DownloadUrl = reader.GetString(7),
            Filename = ReadString(reader, 8),
            PackageStoragePath = ReadString(reader, 9),
            SizeBytes = reader.IsDBNull(10) ? null : reader.GetInt64(10),
            CacheSizeBytes = reader.IsDBNull(11) ? null : reader.GetInt64(11),
            ProtocolsJson = reader.GetString(12),
            HashesJson = reader.GetString(13),
            Shasum = ReadString(reader, 14),
            SigningKeysJson = ReadString(reader, 15),
            State = reader.GetString(16),
            LastError = ReadString(reader, 17),
            HttpStatusCode = reader.IsDBNull(18) ? null : reader.GetInt32(18),
            LastSyncAt = ReadDateTime(reader, 19),
            CreatedAt = reader.GetDateTime(20),
            UpdatedAt = reader.GetDateTime(21)
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

    private static string DefaultProviderDownloadUrl(
        string providerNamespace,
        string type,
        string version,
        string os,
        string arch) =>
        $"https://registry.terraform.io/v1/providers/{providerNamespace}/{type}/{version}/download/{os}/{arch}";
}
