using Npgsql;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.PostgreSQL;

public class PostgreSqlVcsSourceService : IVcsSourceService
{
    private readonly string _connectionString;

    public PostgreSqlVcsSourceService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IEnumerable<VcsSource>> ListVcsSourcesAsync(string userId)
    {
        var sources = new List<VcsSource>();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"SELECT id, user_id, namespace, name, provider, repo_owner, repo_name, connection_id, is_active,
                           tag_pattern, last_published_version, last_sync_status, last_sync_at, last_sync_error,
                           created_at, updated_at
                    FROM vcs_sources
                    WHERE user_id = @userId
                    ORDER BY created_at DESC";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@userId", userId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            sources.Add(MapVcsSource(reader));
        }

        return sources;
    }

    public async Task<VcsSource> CreateVcsSourceAsync(string userId, string moduleNamespace, string name, string provider, string repoOwner, string repoName, Guid connectionId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var vcsSource = new VcsSource
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Namespace = moduleNamespace,
            Name = name,
            Provider = provider,
            RepoOwner = repoOwner,
            RepoName = repoName,
            ConnectionId = connectionId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var sql = @"INSERT INTO vcs_sources (id, user_id, namespace, name, provider, repo_owner, repo_name, connection_id, is_active, created_at, updated_at)
                    VALUES (@id, @userId, @moduleNamespace, @name, @provider, @repoOwner, @repoName, @connectionId, @isActive, @createdAt, @updatedAt)";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", vcsSource.Id);
        cmd.Parameters.AddWithValue("@userId", vcsSource.UserId);
        cmd.Parameters.AddWithValue("moduleNamespace", vcsSource.Namespace);
        cmd.Parameters.AddWithValue("@name", vcsSource.Name);
        cmd.Parameters.AddWithValue("@provider", vcsSource.Provider);
        cmd.Parameters.AddWithValue("@repoOwner", vcsSource.RepoOwner);
        cmd.Parameters.AddWithValue("@repoName", vcsSource.RepoName);
        cmd.Parameters.AddWithValue("@connectionId", vcsSource.ConnectionId);
        cmd.Parameters.AddWithValue("@isActive", vcsSource.IsActive);
        cmd.Parameters.AddWithValue("@createdAt", vcsSource.CreatedAt);
        cmd.Parameters.AddWithValue("@updatedAt", vcsSource.UpdatedAt);

        await cmd.ExecuteNonQueryAsync();
        return vcsSource;
    }

    public async Task<VcsSource?> UpdateVcsSourceAsync(Guid id, string userId, string? repoOwner, string? repoName, Guid? connectionId, bool? isActive)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var setClauses = new List<string> { "updated_at = @updatedAt" };
        var parameters = new List<NpgsqlParameter>
        {
            new("@id", id),
            new("@userId", userId),
            new("@updatedAt", DateTime.UtcNow)
        };

        if (repoOwner != null)
        {
            setClauses.Add("repo_owner = @repoOwner");
            parameters.Add(new NpgsqlParameter("@repoOwner", repoOwner));
        }

        if (repoName != null)
        {
            setClauses.Add("repo_name = @repoName");
            parameters.Add(new NpgsqlParameter("@repoName", repoName));
        }

        if (connectionId.HasValue)
        {
            setClauses.Add("connection_id = @connectionId");
            parameters.Add(new NpgsqlParameter("@connectionId", connectionId.Value));
        }

        if (isActive.HasValue)
        {
            setClauses.Add("is_active = @isActive");
            parameters.Add(new NpgsqlParameter("@isActive", isActive.Value));
        }

        var sql = $"UPDATE vcs_sources SET {string.Join(", ", setClauses)} WHERE id = @id AND user_id = @userId";
        await using var cmd = new NpgsqlCommand(sql, connection);
        foreach (var p in parameters) cmd.Parameters.Add(p);
        await cmd.ExecuteNonQueryAsync();

        // SELECT the row back
        var selectSql = @"SELECT id, user_id, namespace, name, provider, repo_owner, repo_name, connection_id, is_active,
                                 tag_pattern, last_published_version, last_sync_status, last_sync_at, last_sync_error,
                                 created_at, updated_at
                          FROM vcs_sources
                          WHERE id = @selectId AND user_id = @selectUserId";
        await using var selectCmd = new NpgsqlCommand(selectSql, connection);
        selectCmd.Parameters.AddWithValue("@selectId", id);
        selectCmd.Parameters.AddWithValue("@selectUserId", userId);

        await using var reader = await selectCmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return MapVcsSource(reader);
    }

    public async Task<bool> DeleteVcsSourceAsync(Guid id, string userId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = "DELETE FROM vcs_sources WHERE id = @id AND user_id = @userId";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@userId", userId);

        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<VcsSource?> GetByRepoAsync(string repoOwner, string repoName)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"SELECT id, user_id, namespace, name, provider, repo_owner, repo_name, connection_id, is_active,
                           tag_pattern, last_published_version, last_sync_status, last_sync_at, last_sync_error,
                           created_at, updated_at
                    FROM vcs_sources
                    WHERE repo_owner = @repoOwner AND repo_name = @repoName AND is_active = true
                    LIMIT 1";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@repoOwner", repoOwner);
        cmd.Parameters.AddWithValue("@repoName", repoName);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return MapVcsSource(reader);
    }

    public async Task<VcsSource?> GetAsync(Guid id)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"SELECT id, user_id, namespace, name, provider, repo_owner, repo_name, connection_id, is_active,
                           tag_pattern, last_published_version, last_sync_status, last_sync_at, last_sync_error,
                           created_at, updated_at
                    FROM vcs_sources
                    WHERE id = @id
                    LIMIT 1";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return MapVcsSource(reader);
    }

    public async Task<VcsSource?> GetByModuleAsync(string moduleNamespace, string name, string provider)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"SELECT id, user_id, namespace, name, provider, repo_owner, repo_name, connection_id, is_active,
                           tag_pattern, last_published_version, last_sync_status, last_sync_at, last_sync_error,
                           created_at, updated_at
                    FROM vcs_sources
                    WHERE namespace = @moduleNamespace AND name = @name AND provider = @provider AND is_active = true
                    LIMIT 1";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("moduleNamespace", moduleNamespace);
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@provider", provider);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return MapVcsSource(reader);
    }

    public async Task<VcsSource?> UpdateSyncStateAsync(Guid id, string status, string? lastPublishedVersion, string? errorMessage)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"UPDATE vcs_sources
                    SET last_sync_status = @status,
                        last_published_version = @lastPublishedVersion,
                        last_sync_error = @error,
                        last_sync_at = @lastSyncAt,
                        updated_at = @updatedAt
                    WHERE id = @id";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@lastPublishedVersion", (object?)lastPublishedVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@error", (object?)errorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lastSyncAt", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow);

        var rows = await cmd.ExecuteNonQueryAsync();
        if (rows == 0) return null;

        return await GetAsync(id);
    }

    private static VcsSource MapVcsSource(NpgsqlDataReader reader)
    {
        return new VcsSource
        {
            Id = reader.GetGuid(0),
            UserId = reader.GetString(1),
            Namespace = reader.GetString(2),
            Name = reader.GetString(3),
            Provider = reader.GetString(4),
            RepoOwner = reader.GetString(5),
            RepoName = reader.GetString(6),
            ConnectionId = reader.GetGuid(7),
            IsActive = reader.GetBoolean(8),
            TagPattern = reader.GetString(9),
            LastPublishedVersion = reader.IsDBNull(10) ? null : reader.GetString(10),
            LastSyncStatus = reader.GetString(11),
            LastSyncAt = reader.IsDBNull(12) ? null : reader.GetDateTime(12),
            LastSyncError = reader.IsDBNull(13) ? null : reader.GetString(13),
            CreatedAt = reader.GetDateTime(14),
            UpdatedAt = reader.GetDateTime(15)
        };
    }
}
