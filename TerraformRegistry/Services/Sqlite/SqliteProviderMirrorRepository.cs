using System.Globalization;
using Microsoft.Data.Sqlite;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services.Sqlite;

public sealed class SqliteProviderMirrorRepository(string connectionString) : IProviderMirrorRepository
{
    public async Task<MirrorProviderIndex?> GetProviderIndexAsync(
        string hostname,
        string providerNamespace,
        string type)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, hostname, namespace, type, versions_json, etag, state, last_error, last_sync_at, created_at, updated_at
            FROM mirror_provider_indexes
            WHERE hostname = $hostname AND namespace = $namespace AND type = $type";
        command.Parameters.AddWithValue("$hostname", hostname);
        command.Parameters.AddWithValue("$namespace", providerNamespace);
        command.Parameters.AddWithValue("$type", type);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapProviderIndex(reader) : null;
    }

    public async Task UpsertProviderIndexAsync(MirrorProviderIndex providerIndex)
    {
        var now = DateTime.UtcNow;

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO mirror_provider_indexes (
                id, hostname, namespace, type, versions_json, etag, state, last_error, last_sync_at, created_at, updated_at)
            VALUES (
                $id, $hostname, $namespace, $type, $versionsJson, $etag, $state, $lastError, $lastSyncAt, $createdAt, $updatedAt)
            ON CONFLICT(hostname, namespace, type) DO UPDATE SET
                versions_json = excluded.versions_json,
                etag = excluded.etag,
                state = excluded.state,
                last_error = excluded.last_error,
                last_sync_at = excluded.last_sync_at,
                updated_at = excluded.updated_at";
        command.Parameters.AddWithValue("$id", providerIndex.Id.ToString());
        command.Parameters.AddWithValue("$hostname", providerIndex.Hostname);
        command.Parameters.AddWithValue("$namespace", providerIndex.Namespace);
        command.Parameters.AddWithValue("$type", providerIndex.Type);
        command.Parameters.AddWithValue("$versionsJson", providerIndex.VersionsJson);
        command.Parameters.AddWithValue("$etag", DbValue(providerIndex.ETag));
        command.Parameters.AddWithValue("$state", providerIndex.State);
        command.Parameters.AddWithValue("$lastError", DbValue(providerIndex.LastError));
        command.Parameters.AddWithValue("$lastSyncAt", DbValue(providerIndex.LastSyncAt));
        command.Parameters.AddWithValue("$createdAt", ToSqliteTimestamp(providerIndex.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", ToSqliteTimestamp(now));

        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<MirrorProviderPackage>> ListProviderPackagesAsync(
        string? q,
        string? state,
        int limit,
        int offset)
    {
        var packages = new List<MirrorProviderPackage>();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"
            SELECT id, hostname, namespace, type, version, os, arch, download_url, filename, package_storage_path,
                   size_bytes, cache_size_bytes, protocols_json, hashes_json, shasum, signing_keys_json, state, last_error,
                   http_status_code, last_sync_at, created_at, updated_at
            FROM mirror_provider_packages
            WHERE 1 = 1";
        var parameters = new List<SqliteParameter>();

        if (!string.IsNullOrWhiteSpace(q))
        {
            sql += @" AND (
                hostname LIKE $q OR namespace LIKE $q OR type LIKE $q OR version LIKE $q OR os LIKE $q OR arch LIKE $q)";
            parameters.Add(new SqliteParameter("$q", $"%{q.Trim()}%"));
        }

        if (!string.IsNullOrWhiteSpace(state))
        {
            sql += " AND state = $state";
            parameters.Add(new SqliteParameter("$state", state.Trim()));
        }

        sql += " ORDER BY hostname, namespace, type, version, os, arch LIMIT $limit OFFSET $offset";
        parameters.Add(new SqliteParameter("$limit", Math.Clamp(limit, 1, 1000)));
        parameters.Add(new SqliteParameter("$offset", Math.Max(0, offset)));

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

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
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, hostname, namespace, type, version, os, arch, download_url, filename, package_storage_path,
                   size_bytes, cache_size_bytes, protocols_json, hashes_json, shasum, signing_keys_json, state, last_error,
                   http_status_code, last_sync_at, created_at, updated_at
            FROM mirror_provider_packages
            WHERE hostname = $hostname AND namespace = $namespace AND type = $type
              AND version = $version AND os = $os AND arch = $arch";
        command.Parameters.AddWithValue("$hostname", hostname);
        command.Parameters.AddWithValue("$namespace", providerNamespace);
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$os", os);
        command.Parameters.AddWithValue("$arch", arch);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapProviderPackage(reader) : null;
    }

    public async Task UpsertProviderPackageAsync(MirrorProviderPackage package)
    {
        var now = DateTime.UtcNow;

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO mirror_provider_packages (
                id, hostname, namespace, type, version, os, arch, download_url, filename, package_storage_path,
                size_bytes, cache_size_bytes, protocols_json, hashes_json, shasum, signing_keys_json, state, last_error,
                http_status_code, last_sync_at, created_at, updated_at)
            VALUES (
                $id, $hostname, $namespace, $type, $version, $os, $arch, $downloadUrl, $filename, $packageStoragePath,
                $sizeBytes, $cacheSizeBytes, $protocolsJson, $hashesJson, $shasum, $signingKeysJson, $state, $lastError,
                $httpStatusCode, $lastSyncAt, $createdAt, $updatedAt)
            ON CONFLICT(hostname, namespace, type, version, os, arch) DO UPDATE SET
                download_url = excluded.download_url,
                filename = excluded.filename,
                package_storage_path = excluded.package_storage_path,
                size_bytes = excluded.size_bytes,
                cache_size_bytes = excluded.cache_size_bytes,
                protocols_json = excluded.protocols_json,
                hashes_json = excluded.hashes_json,
                shasum = excluded.shasum,
                signing_keys_json = excluded.signing_keys_json,
                state = excluded.state,
                last_error = excluded.last_error,
                http_status_code = excluded.http_status_code,
                last_sync_at = excluded.last_sync_at,
                updated_at = excluded.updated_at";
        AddPackageParameters(command, package, now);

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
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO mirror_provider_packages (
                id, hostname, namespace, type, version, os, arch, download_url, protocols_json, hashes_json,
                state, last_error, http_status_code, created_at, updated_at)
            VALUES (
                $id, $hostname, $namespace, $type, $version, $os, $arch, $downloadUrl, '[]', '[]',
                'failed', $error, $httpStatusCode, $createdAt, $updatedAt)
            ON CONFLICT(hostname, namespace, type, version, os, arch) DO UPDATE SET
                state = 'failed',
                last_error = excluded.last_error,
                http_status_code = excluded.http_status_code,
                updated_at = excluded.updated_at";
        var now = DateTime.UtcNow;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$hostname", hostname);
        command.Parameters.AddWithValue("$namespace", providerNamespace);
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$os", os);
        command.Parameters.AddWithValue("$arch", arch);
        command.Parameters.AddWithValue("$downloadUrl", DefaultProviderDownloadUrl(providerNamespace, type, version, os, arch));
        command.Parameters.AddWithValue("$error", errorMessage);
        command.Parameters.AddWithValue("$httpStatusCode", DbValue(httpStatusCode));
        command.Parameters.AddWithValue("$createdAt", ToSqliteTimestamp(now));
        command.Parameters.AddWithValue("$updatedAt", ToSqliteTimestamp(now));

        await command.ExecuteNonQueryAsync();
    }

    private static void AddPackageParameters(SqliteCommand command, MirrorProviderPackage package, DateTime updatedAt)
    {
        command.Parameters.AddWithValue("$id", package.Id.ToString());
        command.Parameters.AddWithValue("$hostname", package.Hostname);
        command.Parameters.AddWithValue("$namespace", package.Namespace);
        command.Parameters.AddWithValue("$type", package.Type);
        command.Parameters.AddWithValue("$version", package.Version);
        command.Parameters.AddWithValue("$os", package.Os);
        command.Parameters.AddWithValue("$arch", package.Arch);
        command.Parameters.AddWithValue("$downloadUrl", package.DownloadUrl);
        command.Parameters.AddWithValue("$filename", DbValue(package.Filename));
        command.Parameters.AddWithValue("$packageStoragePath", DbValue(package.PackageStoragePath));
        command.Parameters.AddWithValue("$sizeBytes", DbValue(package.SizeBytes));
        command.Parameters.AddWithValue("$cacheSizeBytes", DbValue(package.CacheSizeBytes));
        command.Parameters.AddWithValue("$protocolsJson", package.ProtocolsJson);
        command.Parameters.AddWithValue("$hashesJson", package.HashesJson);
        command.Parameters.AddWithValue("$shasum", DbValue(package.Shasum));
        command.Parameters.AddWithValue("$signingKeysJson", DbValue(package.SigningKeysJson));
        command.Parameters.AddWithValue("$state", package.State);
        command.Parameters.AddWithValue("$lastError", DbValue(package.LastError));
        command.Parameters.AddWithValue("$httpStatusCode", DbValue(package.HttpStatusCode));
        command.Parameters.AddWithValue("$lastSyncAt", DbValue(package.LastSyncAt));
        command.Parameters.AddWithValue("$createdAt", ToSqliteTimestamp(package.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", ToSqliteTimestamp(updatedAt));
    }

    private static MirrorProviderIndex MapProviderIndex(SqliteDataReader reader)
    {
        return new MirrorProviderIndex
        {
            Id = Guid.Parse(reader.GetString(0)),
            Hostname = reader.GetString(1),
            Namespace = reader.GetString(2),
            Type = reader.GetString(3),
            VersionsJson = reader.GetString(4),
            ETag = ReadString(reader, 5),
            State = reader.GetString(6),
            LastError = ReadString(reader, 7),
            LastSyncAt = ReadDateTime(reader, 8),
            CreatedAt = ReadRequiredDateTime(reader, 9),
            UpdatedAt = ReadRequiredDateTime(reader, 10)
        };
    }

    private static MirrorProviderPackage MapProviderPackage(SqliteDataReader reader)
    {
        return new MirrorProviderPackage
        {
            Id = Guid.Parse(reader.GetString(0)),
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
            CreatedAt = ReadRequiredDateTime(reader, 20),
            UpdatedAt = ReadRequiredDateTime(reader, 21)
        };
    }

    private static object DbValue(string? value) => (object?)value ?? DBNull.Value;

    private static object DbValue(long? value) => value.HasValue ? value.Value : DBNull.Value;

    private static object DbValue(int? value) => value.HasValue ? value.Value : DBNull.Value;

    private static object DbValue(DateTime? value) =>
        value.HasValue ? ToSqliteTimestamp(value.Value) : DBNull.Value;

    private static string? ReadString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static DateTime? ReadDateTime(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ReadRequiredDateTime(reader, ordinal);

    private static DateTime ReadRequiredDateTime(SqliteDataReader reader, int ordinal) =>
        DateTime.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string ToSqliteTimestamp(DateTime value) =>
        value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    private static string DefaultProviderDownloadUrl(
        string providerNamespace,
        string type,
        string version,
        string os,
        string arch) =>
        $"https://registry.terraform.io/v1/providers/{providerNamespace}/{type}/{version}/download/{os}/{arch}";
}
