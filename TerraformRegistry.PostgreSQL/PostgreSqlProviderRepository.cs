using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Utilities;
using TerraformRegistry.Models;

namespace TerraformRegistry.PostgreSQL;

public sealed class PostgreSqlProviderRepository : IProviderRepository
{
    private readonly string _connectionString;

    public PostgreSqlProviderRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<TerraformProvider>> ListProvidersAsync(string? q, int offset, int limit)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(@"
            SELECT id, namespace, type, display_name, description, source_repository_url, created_by, created_at, updated_at, deleted_at
            FROM providers
            WHERE deleted_at IS NULL
              AND (CAST(@like AS text) IS NULL OR namespace ILIKE @like OR type ILIKE @like OR display_name ILIKE @like OR description ILIKE @like)
            ORDER BY namespace, type
            LIMIT @limit OFFSET @offset", connection);
        command.Parameters.Add(new NpgsqlParameter("@like", NpgsqlDbType.Text)
        {
            Value = string.IsNullOrWhiteSpace(q) ? DBNull.Value : $"%{q}%"
        });
        command.Parameters.AddWithValue("@limit", limit);
        command.Parameters.AddWithValue("@offset", offset);

        var providers = new List<TerraformProvider>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            providers.Add(MapProvider(reader));
        }

        return providers;
    }

    public async Task<TerraformProvider?> GetProviderAsync(string providerNamespace, string type)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(@"
            SELECT id, namespace, type, display_name, description, source_repository_url, created_by, created_at, updated_at, deleted_at
            FROM providers
            WHERE namespace = @providerNamespace AND type = @type AND deleted_at IS NULL", connection);
        command.Parameters.AddWithValue("providerNamespace", providerNamespace);
        command.Parameters.AddWithValue("@type", type);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapProvider(reader) : null;
    }

    public async Task<TerraformProvider> CreateProviderAsync(TerraformProvider provider)
    {
        provider.Id = provider.Id == Guid.Empty ? Guid.NewGuid() : provider.Id;
        provider.CreatedAt = provider.CreatedAt == default ? DateTime.UtcNow : provider.CreatedAt;
        provider.UpdatedAt = provider.UpdatedAt == default ? provider.CreatedAt : provider.UpdatedAt;

        try
        {
            await using var connection = await OpenConnectionAsync();
            await using var command = new NpgsqlCommand(@"
                INSERT INTO providers (id, namespace, type, display_name, description, source_repository_url, created_by, created_at, updated_at)
                VALUES (@id, @providerNamespace, @type, @displayName, @description, @sourceRepositoryUrl, @createdBy, @createdAt, @updatedAt)", connection);
            command.Parameters.AddWithValue("@id", provider.Id);
            command.Parameters.AddWithValue("providerNamespace", provider.Namespace);
            command.Parameters.AddWithValue("@type", provider.Type);
            command.Parameters.AddWithValue("@displayName", DbValue(provider.DisplayName));
            command.Parameters.AddWithValue("@description", DbValue(provider.Description));
            command.Parameters.AddWithValue("@sourceRepositoryUrl", DbValue(provider.SourceRepositoryUrl));
            command.Parameters.AddWithValue("@createdBy", DbValue(provider.CreatedBy));
            command.Parameters.AddWithValue("@createdAt", provider.CreatedAt.ToUniversalTime());
            command.Parameters.AddWithValue("@updatedAt", provider.UpdatedAt.ToUniversalTime());
            await command.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new InvalidOperationException($"Provider {provider.Namespace}/{provider.Type} already exists", ex);
        }

        return provider;
    }

    public async Task<bool> UpdateProviderAsync(string providerNamespace, string type, string? displayName, string? description, string? sourceRepositoryUrl)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(@"
            UPDATE providers
            SET display_name = @displayName,
                description = @description,
                source_repository_url = @sourceRepositoryUrl,
                updated_at = @updatedAt
            WHERE namespace = @providerNamespace AND type = @type AND deleted_at IS NULL", connection);
        command.Parameters.AddWithValue("@displayName", DbValue(displayName));
        command.Parameters.AddWithValue("@description", DbValue(description));
        command.Parameters.AddWithValue("@sourceRepositoryUrl", DbValue(sourceRepositoryUrl));
        command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow);
        command.Parameters.AddWithValue("providerNamespace", providerNamespace);
        command.Parameters.AddWithValue("@type", type);
        return await command.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> DeleteProviderAsync(string providerNamespace, string type)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(@"
            UPDATE providers
            SET deleted_at = @deletedAt,
                updated_at = @deletedAt
            WHERE namespace = @providerNamespace AND type = @type AND deleted_at IS NULL", connection);
        command.Parameters.AddWithValue("@deletedAt", DateTime.UtcNow);
        command.Parameters.AddWithValue("providerNamespace", providerNamespace);
        command.Parameters.AddWithValue("@type", type);
        return await command.ExecuteNonQueryAsync() > 0;
    }

    public async Task<IReadOnlyList<ProviderVersionEntry>> GetProviderVersionsAsync(string providerNamespace, string type)
    {
        await using var connection = await OpenConnectionAsync();
        var versionRows = new List<(Guid Id, string Version, string[] Protocols)>();
        await using (var command = new NpgsqlCommand(@"
            SELECT pv.id, pv.version, pv.protocols::text
            FROM provider_versions pv
            INNER JOIN providers p ON p.id = pv.provider_id
            WHERE p.namespace = @providerNamespace AND p.type = @type
              AND p.deleted_at IS NULL AND pv.deleted_at IS NULL
              AND NULLIF(BTRIM(pv.shasums_storage_path), '') IS NOT NULL
              AND NULLIF(BTRIM(pv.shasums_signature_storage_path), '') IS NOT NULL
              AND EXISTS (
                  SELECT 1
                  FROM provider_platforms pp
                  WHERE pp.provider_version_id = pv.id
                    AND NULLIF(BTRIM(pp.package_storage_path), '') IS NOT NULL
              )", connection))
        {
            command.Parameters.AddWithValue("providerNamespace", providerNamespace);
            command.Parameters.AddWithValue("@type", type);

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                versionRows.Add((reader.GetGuid(0), reader.GetString(1), DeserializeProtocols(reader.GetString(2))));
            }
        }

        var entries = new List<ProviderVersionEntry>();
        foreach (var row in versionRows.OrderByDescending(row => row.Version, SemVerVersionComparer.Instance))
        {
            entries.Add(new ProviderVersionEntry
            {
                Version = row.Version,
                Protocols = row.Protocols,
                Platforms = await GetPlatformEntriesAsync(connection, row.Id)
            });
        }

        return entries;
    }

    public async Task<IReadOnlyList<ProviderManagementVersionEntry>> GetProviderManagementVersionsAsync(string providerNamespace, string type)
    {
        await using var connection = await OpenConnectionAsync();
        var versionRows = new List<(Guid Id, string Version, string[] Protocols, string KeyId, bool HasShasums,
            bool HasShasumsSignature, DateTime PublishedAt)>();

        await using (var command = new NpgsqlCommand(@"
            SELECT pv.id, pv.version, pv.protocols::text, pv.key_id, pv.shasums_storage_path,
                   pv.shasums_signature_storage_path, pv.published_at
            FROM provider_versions pv
            INNER JOIN providers p ON p.id = pv.provider_id
            WHERE p.namespace = @providerNamespace AND p.type = @type
              AND p.deleted_at IS NULL AND pv.deleted_at IS NULL", connection))
        {
            command.Parameters.AddWithValue("providerNamespace", providerNamespace);
            command.Parameters.AddWithValue("@type", type);

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var shasumsStoragePath = ReadNullableString(reader, 4);
                var shasumsSignatureStoragePath = ReadNullableString(reader, 5);
                versionRows.Add((
                    reader.GetGuid(0),
                    reader.GetString(1),
                    DeserializeProtocols(reader.GetString(2)),
                    reader.GetString(3),
                    !string.IsNullOrWhiteSpace(shasumsStoragePath),
                    !string.IsNullOrWhiteSpace(shasumsSignatureStoragePath),
                    reader.GetDateTime(6).ToUniversalTime()));
            }
        }

        var entries = new List<ProviderManagementVersionEntry>();
        foreach (var row in versionRows.OrderByDescending(row => row.Version, SemVerVersionComparer.Instance))
        {
            entries.Add(new ProviderManagementVersionEntry
            {
                Id = row.Id.ToString(),
                Version = row.Version,
                Protocols = row.Protocols,
                KeyId = row.KeyId,
                HasShasums = row.HasShasums,
                HasShasumsSignature = row.HasShasumsSignature,
                PublishedAt = row.PublishedAt,
                Platforms = await GetManagementPlatformEntriesAsync(connection, row.Id)
            });
        }

        return entries;
    }

    public async Task<ProviderVersion?> GetProviderVersionAsync(string providerNamespace, string type, string version)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(@"
            SELECT pv.id, pv.provider_id, pv.version, pv.protocols::text, pv.key_id, pv.shasums_storage_path,
                   pv.shasums_signature_storage_path, pv.published_at, pv.deleted_at
            FROM provider_versions pv
            INNER JOIN providers p ON p.id = pv.provider_id
            WHERE p.namespace = @providerNamespace AND p.type = @type AND pv.version = @version
              AND p.deleted_at IS NULL AND pv.deleted_at IS NULL", connection);
        command.Parameters.AddWithValue("providerNamespace", providerNamespace);
        command.Parameters.AddWithValue("@type", type);
        command.Parameters.AddWithValue("@version", version);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapVersion(reader) : null;
    }

    public async Task<ProviderVersion> CreateProviderVersionAsync(Guid providerId, string version, string[] protocols, string keyId)
    {
        var providerVersion = new ProviderVersion
        {
            Id = Guid.NewGuid(),
            ProviderId = providerId,
            Version = version,
            Protocols = protocols,
            KeyId = keyId,
            PublishedAt = DateTime.UtcNow
        };

        try
        {
            await using var connection = await OpenConnectionAsync();
            await using var command = new NpgsqlCommand(@"
                INSERT INTO provider_versions (id, provider_id, version, protocols, key_id, published_at)
                VALUES (@id, @providerId, @version, @protocols, @keyId, @publishedAt)", connection);
            command.Parameters.AddWithValue("@id", providerVersion.Id);
            command.Parameters.AddWithValue("@providerId", providerVersion.ProviderId);
            command.Parameters.AddWithValue("@version", providerVersion.Version);
            var protocolsParameter = command.Parameters.AddWithValue("@protocols", SerializeProtocols(providerVersion.Protocols));
            protocolsParameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
            command.Parameters.AddWithValue("@keyId", providerVersion.KeyId);
            command.Parameters.AddWithValue("@publishedAt", providerVersion.PublishedAt);
            await command.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new InvalidOperationException($"Provider version {providerId}/{version} already exists", ex);
        }

        return providerVersion;
    }

    public Task<bool> SetVersionShasumsPathAsync(Guid versionId, string storagePath) =>
        UpdateSinglePathAsync("provider_versions", "shasums_storage_path", "id", versionId, storagePath);

    public Task<bool> SetVersionShasumsSignaturePathAsync(Guid versionId, string storagePath) =>
        UpdateSinglePathAsync("provider_versions", "shasums_signature_storage_path", "id", versionId, storagePath);

    public async Task<bool> DeleteProviderVersionAsync(string providerNamespace, string type, string version)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(@"
            UPDATE provider_versions
            SET deleted_at = @deletedAt
            WHERE version = @version
              AND provider_id IN (
                  SELECT id FROM providers WHERE namespace = @providerNamespace AND type = @type AND deleted_at IS NULL
              )
              AND deleted_at IS NULL", connection);
        command.Parameters.AddWithValue("@deletedAt", DateTime.UtcNow);
        command.Parameters.AddWithValue("providerNamespace", providerNamespace);
        command.Parameters.AddWithValue("@type", type);
        command.Parameters.AddWithValue("@version", version);
        return await command.ExecuteNonQueryAsync() > 0;
    }

    public async Task<IReadOnlyList<string>> GetProviderArtifactStoragePathsAsync(string providerNamespace, string type, string? version, string? os, string? arch)
    {
        await using var connection = await OpenConnectionAsync();
        var paths = new HashSet<string>(StringComparer.Ordinal);

        await using (var versionCommand = new NpgsqlCommand(@"
            SELECT pv.shasums_storage_path, pv.shasums_signature_storage_path
            FROM provider_versions pv
            INNER JOIN providers p ON p.id = pv.provider_id
            WHERE p.namespace = @providerNamespace AND p.type = @type
              AND p.deleted_at IS NULL AND pv.deleted_at IS NULL
              AND (CAST(@version AS text) IS NULL OR pv.version = @version)", connection))
        {
            versionCommand.Parameters.AddWithValue("providerNamespace", providerNamespace);
            versionCommand.Parameters.AddWithValue("@type", type);
            versionCommand.Parameters.AddWithValue("@version", DbValue(version));

            await using var reader = await versionCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                AddStoragePath(paths, ReadNullableString(reader, 0));
                AddStoragePath(paths, ReadNullableString(reader, 1));
            }
        }

        await using (var platformCommand = new NpgsqlCommand(@"
            SELECT pp.package_storage_path
            FROM provider_platforms pp
            INNER JOIN provider_versions pv ON pv.id = pp.provider_version_id
            INNER JOIN providers p ON p.id = pv.provider_id
            WHERE p.namespace = @providerNamespace AND p.type = @type
              AND p.deleted_at IS NULL AND pv.deleted_at IS NULL
              AND (CAST(@version AS text) IS NULL OR pv.version = @version)
              AND (CAST(@os AS text) IS NULL OR pp.os = @os)
              AND (CAST(@arch AS text) IS NULL OR pp.arch = @arch)", connection))
        {
            platformCommand.Parameters.AddWithValue("providerNamespace", providerNamespace);
            platformCommand.Parameters.AddWithValue("@type", type);
            platformCommand.Parameters.AddWithValue("@version", DbValue(version));
            platformCommand.Parameters.AddWithValue("@os", DbValue(os));
            platformCommand.Parameters.AddWithValue("@arch", DbValue(arch));

            await using var reader = await platformCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                AddStoragePath(paths, ReadNullableString(reader, 0));
            }
        }

        return paths.OrderBy(path => path, StringComparer.Ordinal).ToList();
    }

    public async Task<IReadOnlyList<ProviderManagementPlatformEntry>> GetProviderManagementPlatformsAsync(string providerNamespace, string type, string version)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(@"
            SELECT pp.id, pp.os, pp.arch, pp.filename, pp.shasum, pp.package_storage_path,
                   pp.size_bytes, pp.uploaded_at
            FROM provider_platforms pp
            INNER JOIN provider_versions pv ON pv.id = pp.provider_version_id
            INNER JOIN providers p ON p.id = pv.provider_id
            WHERE p.namespace = @providerNamespace AND p.type = @type AND pv.version = @version
              AND p.deleted_at IS NULL AND pv.deleted_at IS NULL
            ORDER BY pp.os, pp.arch", connection);
        command.Parameters.AddWithValue("providerNamespace", providerNamespace);
        command.Parameters.AddWithValue("@type", type);
        command.Parameters.AddWithValue("@version", version);

        var platforms = new List<ProviderManagementPlatformEntry>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            platforms.Add(MapManagementPlatformEntry(reader));
        }

        return platforms;
    }

    public async Task<ProviderPlatform?> GetProviderPlatformAsync(string providerNamespace, string type, string version, string os, string arch)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(@"
            SELECT pp.id, pp.provider_version_id, pp.os, pp.arch, pp.filename, pp.shasum,
                   pp.package_storage_path, pp.size_bytes, pp.uploaded_at
            FROM provider_platforms pp
            INNER JOIN provider_versions pv ON pv.id = pp.provider_version_id
            INNER JOIN providers p ON p.id = pv.provider_id
            WHERE p.namespace = @providerNamespace AND p.type = @type AND pv.version = @version
              AND pp.os = @os AND pp.arch = @arch
              AND p.deleted_at IS NULL AND pv.deleted_at IS NULL", connection);
        command.Parameters.AddWithValue("providerNamespace", providerNamespace);
        command.Parameters.AddWithValue("@type", type);
        command.Parameters.AddWithValue("@version", version);
        command.Parameters.AddWithValue("@os", os);
        command.Parameters.AddWithValue("@arch", arch);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapPlatform(reader) : null;
    }

    public async Task<ProviderPlatform> CreateProviderPlatformAsync(Guid versionId, string os, string arch, string filename, string shasum)
    {
        var platform = new ProviderPlatform
        {
            Id = Guid.NewGuid(),
            ProviderVersionId = versionId,
            Os = os,
            Arch = arch,
            Filename = filename,
            Shasum = shasum
        };

        try
        {
            await using var connection = await OpenConnectionAsync();
            await using var command = new NpgsqlCommand(@"
                INSERT INTO provider_platforms (id, provider_version_id, os, arch, filename, shasum, size_bytes)
                VALUES (@id, @versionId, @os, @arch, @filename, @shasum, 0)", connection);
            command.Parameters.AddWithValue("@id", platform.Id);
            command.Parameters.AddWithValue("@versionId", platform.ProviderVersionId);
            command.Parameters.AddWithValue("@os", platform.Os);
            command.Parameters.AddWithValue("@arch", platform.Arch);
            command.Parameters.AddWithValue("@filename", platform.Filename);
            command.Parameters.AddWithValue("@shasum", platform.Shasum);
            await command.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new InvalidOperationException($"Provider platform {versionId}/{os}/{arch} already exists", ex);
        }

        return platform;
    }

    public async Task<bool> SetPlatformPackagePathAsync(Guid platformId, string storagePath, long sizeBytes)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(@"
            UPDATE provider_platforms
            SET package_storage_path = @storagePath,
                size_bytes = @sizeBytes,
                uploaded_at = @uploadedAt
            WHERE id = @id", connection);
        command.Parameters.AddWithValue("@storagePath", storagePath);
        command.Parameters.AddWithValue("@sizeBytes", sizeBytes);
        command.Parameters.AddWithValue("@uploadedAt", DateTime.UtcNow);
        command.Parameters.AddWithValue("@id", platformId);
        return await command.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> DeleteProviderPlatformAsync(string providerNamespace, string type, string version, string os, string arch)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(@"
            DELETE FROM provider_platforms
            WHERE os = @os AND arch = @arch
              AND provider_version_id IN (
                  SELECT pv.id
                  FROM provider_versions pv
                  INNER JOIN providers p ON p.id = pv.provider_id
                  WHERE p.namespace = @providerNamespace AND p.type = @type AND pv.version = @version
                    AND p.deleted_at IS NULL AND pv.deleted_at IS NULL
              )", connection);
        command.Parameters.AddWithValue("providerNamespace", providerNamespace);
        command.Parameters.AddWithValue("@type", type);
        command.Parameters.AddWithValue("@version", version);
        command.Parameters.AddWithValue("@os", os);
        command.Parameters.AddWithValue("@arch", arch);
        return await command.ExecuteNonQueryAsync() > 0;
    }

    public async Task<IReadOnlyList<ProviderGpgKey>> ListGpgKeysAsync(string providerNamespace)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(@"
            SELECT id, namespace, key_id, ascii_armor, trust_signature, source, source_url, created_at, revoked_at
            FROM provider_gpg_keys
            WHERE namespace = @providerNamespace AND revoked_at IS NULL
            ORDER BY created_at DESC", connection);
        command.Parameters.AddWithValue("providerNamespace", providerNamespace);

        var keys = new List<ProviderGpgKey>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            keys.Add(MapGpgKey(reader));
        }

        return keys;
    }

    public async Task<ProviderGpgKey?> GetGpgKeyAsync(string providerNamespace, string keyId)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(@"
            SELECT id, namespace, key_id, ascii_armor, trust_signature, source, source_url, created_at, revoked_at
            FROM provider_gpg_keys
            WHERE namespace = @providerNamespace AND key_id = @keyId AND revoked_at IS NULL", connection);
        command.Parameters.AddWithValue("providerNamespace", providerNamespace);
        command.Parameters.AddWithValue("@keyId", keyId);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapGpgKey(reader) : null;
    }

    public async Task<ProviderGpgKey> AddGpgKeyAsync(ProviderGpgKey key)
    {
        key.Id = key.Id == Guid.Empty ? Guid.NewGuid() : key.Id;
        key.CreatedAt = key.CreatedAt == default ? DateTime.UtcNow : key.CreatedAt;

        try
        {
            await using var connection = await OpenConnectionAsync();
            await using var command = new NpgsqlCommand(@"
                INSERT INTO provider_gpg_keys (id, namespace, key_id, ascii_armor, trust_signature, source, source_url, created_at)
                VALUES (@id, @providerNamespace, @keyId, @asciiArmor, @trustSignature, @source, @sourceUrl, @createdAt)", connection);
            command.Parameters.AddWithValue("@id", key.Id);
            command.Parameters.AddWithValue("providerNamespace", key.Namespace);
            command.Parameters.AddWithValue("@keyId", key.KeyId);
            command.Parameters.AddWithValue("@asciiArmor", key.AsciiArmor);
            command.Parameters.AddWithValue("@trustSignature", DbValue(key.TrustSignature));
            command.Parameters.AddWithValue("@source", DbValue(key.Source));
            command.Parameters.AddWithValue("@sourceUrl", DbValue(key.SourceUrl));
            command.Parameters.AddWithValue("@createdAt", key.CreatedAt.ToUniversalTime());
            await command.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new InvalidOperationException($"GPG key {key.Namespace}/{key.KeyId} already exists", ex);
        }

        return key;
    }

    public async Task<bool> ProviderGpgKeyIsReferencedByActiveVersionsAsync(string providerNamespace, string keyId)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(@"
            SELECT 1
            FROM provider_versions pv
            INNER JOIN providers p ON p.id = pv.provider_id
            WHERE p.namespace = @providerNamespace
              AND pv.key_id = @keyId
              AND p.deleted_at IS NULL
              AND pv.deleted_at IS NULL
            LIMIT 1", connection);
        command.Parameters.AddWithValue("providerNamespace", providerNamespace);
        command.Parameters.AddWithValue("@keyId", keyId);

        var result = await command.ExecuteScalarAsync();
        return result != null;
    }

    public async Task<bool> RevokeGpgKeyAsync(string providerNamespace, string keyId)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(@"
            UPDATE provider_gpg_keys
            SET revoked_at = @revokedAt
            WHERE namespace = @providerNamespace AND key_id = @keyId AND revoked_at IS NULL", connection);
        command.Parameters.AddWithValue("@revokedAt", DateTime.UtcNow);
        command.Parameters.AddWithValue("providerNamespace", providerNamespace);
        command.Parameters.AddWithValue("@keyId", keyId);
        return await command.ExecuteNonQueryAsync() > 0;
    }

    public async Task RecordProviderDownloadAsync(Guid? providerId, string providerNamespace, string type, string version, string os,
        string arch, string? clientIp, string? userAgent)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(@"
            INSERT INTO provider_downloads (provider_id, namespace, type, version, os, arch, download_time, client_ip, user_agent)
            VALUES (@providerId, @providerNamespace, @type, @version, @os, @arch, @downloadTime, @clientIp, @userAgent)", connection);
        command.Parameters.AddWithValue("@providerId", providerId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("providerNamespace", providerNamespace);
        command.Parameters.AddWithValue("@type", type);
        command.Parameters.AddWithValue("@version", version);
        command.Parameters.AddWithValue("@os", os);
        command.Parameters.AddWithValue("@arch", arch);
        command.Parameters.AddWithValue("@downloadTime", DateTime.UtcNow);
        command.Parameters.AddWithValue("@clientIp", DbValue(clientIp));
        command.Parameters.AddWithValue("@userAgent", DbValue(userAgent));
        await command.ExecuteNonQueryAsync();
    }

    private async Task<bool> UpdateSinglePathAsync(string table, string column, string idColumn, Guid id, string storagePath)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand($"UPDATE {table} SET {column} = @storagePath WHERE {idColumn} = @id", connection);
        command.Parameters.AddWithValue("@storagePath", storagePath);
        command.Parameters.AddWithValue("@id", id);
        return await command.ExecuteNonQueryAsync() > 0;
    }

    private static async Task<List<ProviderPlatformEntry>> GetPlatformEntriesAsync(NpgsqlConnection connection, Guid versionId)
    {
        await using var command = new NpgsqlCommand(@"
            SELECT os, arch
            FROM provider_platforms
            WHERE provider_version_id = @versionId
              AND NULLIF(BTRIM(package_storage_path), '') IS NOT NULL
            ORDER BY os, arch", connection);
        command.Parameters.AddWithValue("@versionId", versionId);

        var platforms = new List<ProviderPlatformEntry>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            platforms.Add(new ProviderPlatformEntry
            {
                Os = reader.GetString(0),
                Arch = reader.GetString(1)
            });
        }

        return platforms;
    }

    private static async Task<List<ProviderManagementPlatformEntry>> GetManagementPlatformEntriesAsync(NpgsqlConnection connection, Guid versionId)
    {
        await using var command = new NpgsqlCommand(@"
            SELECT id, os, arch, filename, shasum, package_storage_path, size_bytes, uploaded_at
            FROM provider_platforms
            WHERE provider_version_id = @versionId
            ORDER BY os, arch", connection);
        command.Parameters.AddWithValue("@versionId", versionId);

        var platforms = new List<ProviderManagementPlatformEntry>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            platforms.Add(MapManagementPlatformEntry(reader));
        }

        return platforms;
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static TerraformProvider MapProvider(NpgsqlDataReader reader)
    {
        return new TerraformProvider
        {
            Id = reader.GetGuid(0),
            Namespace = reader.GetString(1),
            Type = reader.GetString(2),
            DisplayName = ReadNullableString(reader, 3),
            Description = ReadNullableString(reader, 4),
            SourceRepositoryUrl = ReadNullableString(reader, 5),
            CreatedBy = ReadNullableString(reader, 6),
            CreatedAt = reader.GetDateTime(7).ToUniversalTime(),
            UpdatedAt = reader.GetDateTime(8).ToUniversalTime(),
            DeletedAt = ReadNullableDateTime(reader, 9)
        };
    }

    private static ProviderVersion MapVersion(NpgsqlDataReader reader)
    {
        return new ProviderVersion
        {
            Id = reader.GetGuid(0),
            ProviderId = reader.GetGuid(1),
            Version = reader.GetString(2),
            Protocols = DeserializeProtocols(reader.GetString(3)),
            KeyId = reader.GetString(4),
            ShasumsStoragePath = ReadNullableString(reader, 5),
            ShasumsSignatureStoragePath = ReadNullableString(reader, 6),
            PublishedAt = reader.GetDateTime(7).ToUniversalTime(),
            DeletedAt = ReadNullableDateTime(reader, 8)
        };
    }

    private static ProviderPlatform MapPlatform(NpgsqlDataReader reader)
    {
        return new ProviderPlatform
        {
            Id = reader.GetGuid(0),
            ProviderVersionId = reader.GetGuid(1),
            Os = reader.GetString(2),
            Arch = reader.GetString(3),
            Filename = reader.GetString(4),
            Shasum = reader.GetString(5),
            PackageStoragePath = ReadNullableString(reader, 6),
            SizeBytes = reader.GetInt64(7),
            UploadedAt = ReadNullableDateTime(reader, 8)
        };
    }

    private static ProviderManagementPlatformEntry MapManagementPlatformEntry(NpgsqlDataReader reader)
    {
        var packageStoragePath = ReadNullableString(reader, 5);
        return new ProviderManagementPlatformEntry
        {
            Id = reader.GetGuid(0).ToString(),
            Os = reader.GetString(1),
            Arch = reader.GetString(2),
            Filename = reader.GetString(3),
            Shasum = reader.GetString(4),
            HasPackage = !string.IsNullOrWhiteSpace(packageStoragePath),
            SizeBytes = reader.GetInt64(6),
            UploadedAt = ReadNullableDateTime(reader, 7)
        };
    }

    private static ProviderGpgKey MapGpgKey(NpgsqlDataReader reader)
    {
        return new ProviderGpgKey
        {
            Id = reader.GetGuid(0),
            Namespace = reader.GetString(1),
            KeyId = reader.GetString(2),
            AsciiArmor = reader.GetString(3),
            TrustSignature = ReadNullableString(reader, 4),
            Source = ReadNullableString(reader, 5),
            SourceUrl = ReadNullableString(reader, 6),
            CreatedAt = reader.GetDateTime(7).ToUniversalTime(),
            RevokedAt = ReadNullableDateTime(reader, 8)
        };
    }

    private static string SerializeProtocols(string[] protocols) => JsonSerializer.Serialize(protocols);

    private static string[] DeserializeProtocols(string json) =>
        JsonSerializer.Deserialize<string[]>(json) ?? [];

    private static string? ReadNullableString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static void AddStoragePath(HashSet<string> paths, string? storagePath)
    {
        if (!string.IsNullOrWhiteSpace(storagePath))
        {
            paths.Add(storagePath);
        }
    }

    private static DateTime? ReadNullableDateTime(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal).ToUniversalTime();

    private static object DbValue(string? value) => string.IsNullOrEmpty(value) ? DBNull.Value : value;
}
