using System.Globalization;
using Microsoft.Data.Sqlite;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services.Sqlite;

public sealed class SqliteModuleMirrorRepository(string connectionString) : IModuleMirrorRepository
{
    public async Task<MirrorModuleVersions?> GetModuleVersionsAsync(
        string hostname,
        string moduleNamespace,
        string name,
        string provider)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, hostname, namespace, name, provider, versions_json, etag, state, last_error, last_sync_at, created_at, updated_at
            FROM mirror_module_versions
            WHERE hostname = $hostname AND namespace = $namespace AND name = $name AND provider = $provider";
        command.Parameters.AddWithValue("$hostname", hostname);
        command.Parameters.AddWithValue("$namespace", moduleNamespace);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$provider", provider);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapModuleVersions(reader) : null;
    }

    public async Task UpsertModuleVersionsAsync(MirrorModuleVersions moduleVersions)
    {
        var now = DateTime.UtcNow;

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO mirror_module_versions (
                id, hostname, namespace, name, provider, versions_json, etag, state, last_error, last_sync_at, created_at, updated_at)
            VALUES (
                $id, $hostname, $namespace, $name, $provider, $versionsJson, $etag, $state, $lastError, $lastSyncAt, $createdAt, $updatedAt)
            ON CONFLICT(hostname, namespace, name, provider) DO UPDATE SET
                versions_json = excluded.versions_json,
                etag = excluded.etag,
                state = excluded.state,
                last_error = excluded.last_error,
                last_sync_at = excluded.last_sync_at,
                updated_at = excluded.updated_at";
        command.Parameters.AddWithValue("$id", moduleVersions.Id.ToString());
        command.Parameters.AddWithValue("$hostname", moduleVersions.Hostname);
        command.Parameters.AddWithValue("$namespace", moduleVersions.Namespace);
        command.Parameters.AddWithValue("$name", moduleVersions.Name);
        command.Parameters.AddWithValue("$provider", moduleVersions.Provider);
        command.Parameters.AddWithValue("$versionsJson", moduleVersions.VersionsJson);
        command.Parameters.AddWithValue("$etag", DbValue(moduleVersions.ETag));
        command.Parameters.AddWithValue("$state", moduleVersions.State);
        command.Parameters.AddWithValue("$lastError", DbValue(moduleVersions.LastError));
        command.Parameters.AddWithValue("$lastSyncAt", DbValue(moduleVersions.LastSyncAt));
        command.Parameters.AddWithValue("$createdAt", ToSqliteTimestamp(moduleVersions.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", ToSqliteTimestamp(now));

        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<MirrorModulePackage>> ListModulePackagesAsync(
        string? q,
        string? state,
        int limit,
        int offset)
    {
        var packages = new List<MirrorModulePackage>();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"
            SELECT id, hostname, namespace, name, provider, version, download_url, source, package_storage_path,
                   size_bytes, metadata_json, state, last_error, http_status_code, last_sync_at, created_at, updated_at
            FROM mirror_module_packages
            WHERE 1 = 1";
        var parameters = new List<SqliteParameter>();

        if (!string.IsNullOrWhiteSpace(q))
        {
            sql += @" AND (
                hostname LIKE $q OR namespace LIKE $q OR name LIKE $q OR provider LIKE $q OR version LIKE $q)";
            parameters.Add(new SqliteParameter("$q", $"%{q.Trim()}%"));
        }

        if (!string.IsNullOrWhiteSpace(state))
        {
            sql += " AND state = $state";
            parameters.Add(new SqliteParameter("$state", state.Trim()));
        }

        sql += " ORDER BY hostname, namespace, name, provider, version LIMIT $limit OFFSET $offset";
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
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, hostname, namespace, name, provider, version, download_url, source, package_storage_path,
                   size_bytes, metadata_json, state, last_error, http_status_code, last_sync_at, created_at, updated_at
            FROM mirror_module_packages
            WHERE hostname = $hostname AND namespace = $namespace AND name = $name
              AND provider = $provider AND version = $version";
        command.Parameters.AddWithValue("$hostname", hostname);
        command.Parameters.AddWithValue("$namespace", moduleNamespace);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$provider", provider);
        command.Parameters.AddWithValue("$version", version);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapModulePackage(reader) : null;
    }

    public async Task UpsertModulePackageAsync(MirrorModulePackage package)
    {
        var now = DateTime.UtcNow;

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO mirror_module_packages (
                id, hostname, namespace, name, provider, version, download_url, source, package_storage_path,
                size_bytes, metadata_json, state, last_error, http_status_code, last_sync_at, created_at, updated_at)
            VALUES (
                $id, $hostname, $namespace, $name, $provider, $version, $downloadUrl, $source, $packageStoragePath,
                $sizeBytes, $metadataJson, $state, $lastError, $httpStatusCode, $lastSyncAt, $createdAt, $updatedAt)
            ON CONFLICT(hostname, namespace, name, provider, version) DO UPDATE SET
                download_url = excluded.download_url,
                source = excluded.source,
                package_storage_path = excluded.package_storage_path,
                size_bytes = excluded.size_bytes,
                metadata_json = excluded.metadata_json,
                state = excluded.state,
                last_error = excluded.last_error,
                http_status_code = excluded.http_status_code,
                last_sync_at = excluded.last_sync_at,
                updated_at = excluded.updated_at";
        AddPackageParameters(command, package, now);

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
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE mirror_module_packages
            SET state = 'failed',
                last_error = $error,
                http_status_code = $httpStatusCode,
                updated_at = $updatedAt
            WHERE hostname = $hostname AND namespace = $namespace AND name = $name
              AND provider = $provider AND version = $version";
        command.Parameters.AddWithValue("$hostname", hostname);
        command.Parameters.AddWithValue("$namespace", moduleNamespace);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$provider", provider);
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$error", errorMessage);
        command.Parameters.AddWithValue("$httpStatusCode", DbValue(httpStatusCode));
        command.Parameters.AddWithValue("$updatedAt", ToSqliteTimestamp(DateTime.UtcNow));

        await command.ExecuteNonQueryAsync();
    }

    private static void AddPackageParameters(SqliteCommand command, MirrorModulePackage package, DateTime updatedAt)
    {
        command.Parameters.AddWithValue("$id", package.Id.ToString());
        command.Parameters.AddWithValue("$hostname", package.Hostname);
        command.Parameters.AddWithValue("$namespace", package.Namespace);
        command.Parameters.AddWithValue("$name", package.Name);
        command.Parameters.AddWithValue("$provider", package.Provider);
        command.Parameters.AddWithValue("$version", package.Version);
        command.Parameters.AddWithValue("$downloadUrl", package.DownloadUrl);
        command.Parameters.AddWithValue("$source", DbValue(package.Source));
        command.Parameters.AddWithValue("$packageStoragePath", DbValue(package.PackageStoragePath));
        command.Parameters.AddWithValue("$sizeBytes", DbValue(package.SizeBytes));
        command.Parameters.AddWithValue("$metadataJson", DbValue(package.MetadataJson));
        command.Parameters.AddWithValue("$state", package.State);
        command.Parameters.AddWithValue("$lastError", DbValue(package.LastError));
        command.Parameters.AddWithValue("$httpStatusCode", DbValue(package.HttpStatusCode));
        command.Parameters.AddWithValue("$lastSyncAt", DbValue(package.LastSyncAt));
        command.Parameters.AddWithValue("$createdAt", ToSqliteTimestamp(package.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", ToSqliteTimestamp(updatedAt));
    }

    private static MirrorModuleVersions MapModuleVersions(SqliteDataReader reader)
    {
        return new MirrorModuleVersions
        {
            Id = Guid.Parse(reader.GetString(0)),
            Hostname = reader.GetString(1),
            Namespace = reader.GetString(2),
            Name = reader.GetString(3),
            Provider = reader.GetString(4),
            VersionsJson = reader.GetString(5),
            ETag = ReadString(reader, 6),
            State = reader.GetString(7),
            LastError = ReadString(reader, 8),
            LastSyncAt = ReadDateTime(reader, 9),
            CreatedAt = ReadRequiredDateTime(reader, 10),
            UpdatedAt = ReadRequiredDateTime(reader, 11)
        };
    }

    private static MirrorModulePackage MapModulePackage(SqliteDataReader reader)
    {
        return new MirrorModulePackage
        {
            Id = Guid.Parse(reader.GetString(0)),
            Hostname = reader.GetString(1),
            Namespace = reader.GetString(2),
            Name = reader.GetString(3),
            Provider = reader.GetString(4),
            Version = reader.GetString(5),
            DownloadUrl = reader.GetString(6),
            Source = ReadString(reader, 7),
            PackageStoragePath = ReadString(reader, 8),
            SizeBytes = reader.IsDBNull(9) ? null : reader.GetInt64(9),
            MetadataJson = ReadString(reader, 10),
            State = reader.GetString(11),
            LastError = ReadString(reader, 12),
            HttpStatusCode = reader.IsDBNull(13) ? null : reader.GetInt32(13),
            LastSyncAt = ReadDateTime(reader, 14),
            CreatedAt = ReadRequiredDateTime(reader, 15),
            UpdatedAt = ReadRequiredDateTime(reader, 16)
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
}
