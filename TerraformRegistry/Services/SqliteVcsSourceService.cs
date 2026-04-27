using Microsoft.Data.Sqlite;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services;

public class SqliteVcsSourceService : IVcsSourceService
{
    private readonly string _connectionString;

    public SqliteVcsSourceService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IEnumerable<VcsSource>> ListVcsSourcesAsync(string userId)
    {
        var sources = new List<VcsSource>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT id, user_id, namespace, name, provider, repo_owner, repo_name, connection_id, is_active,
                                   tag_pattern, last_published_version, last_sync_status, last_sync_at, last_sync_error,
                                   created_at, updated_at
                            FROM vcs_sources
                            WHERE user_id = $userId
                            ORDER BY created_at DESC";
        cmd.Parameters.AddWithValue("$userId", userId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            sources.Add(MapVcsSource(reader));
        }

        return sources;
    }

    public async Task<VcsSource> CreateVcsSourceAsync(string userId, string @namespace, string name, string provider, string repoOwner, string repoName, Guid connectionId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var vcsSource = new VcsSource
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Namespace = @namespace,
            Name = name,
            Provider = provider,
            RepoOwner = repoOwner,
            RepoName = repoName,
            ConnectionId = connectionId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO vcs_sources (id, user_id, namespace, name, provider, repo_owner, repo_name, connection_id, is_active, created_at, updated_at)
                            VALUES ($id, $userId, $namespace, $name, $provider, $repoOwner, $repoName, $connectionId, $isActive, $createdAt, $updatedAt)";
        cmd.Parameters.AddWithValue("$id", vcsSource.Id.ToString());
        cmd.Parameters.AddWithValue("$userId", vcsSource.UserId);
        cmd.Parameters.AddWithValue("$namespace", vcsSource.Namespace);
        cmd.Parameters.AddWithValue("$name", vcsSource.Name);
        cmd.Parameters.AddWithValue("$provider", vcsSource.Provider);
        cmd.Parameters.AddWithValue("$repoOwner", vcsSource.RepoOwner);
        cmd.Parameters.AddWithValue("$repoName", vcsSource.RepoName);
        cmd.Parameters.AddWithValue("$connectionId", vcsSource.ConnectionId.ToString());
        cmd.Parameters.AddWithValue("$isActive", vcsSource.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("$createdAt", vcsSource.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$updatedAt", vcsSource.UpdatedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
        return vcsSource;
    }

    public async Task<VcsSource?> UpdateVcsSourceAsync(Guid id, string userId, string? repoOwner, string? repoName, Guid? connectionId, bool? isActive)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // Check ownership
        await using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT id FROM vcs_sources WHERE id = $id AND user_id = $userId";
        checkCmd.Parameters.AddWithValue("$id", id.ToString());
        checkCmd.Parameters.AddWithValue("$userId", userId);
        var exists = await checkCmd.ExecuteScalarAsync();
        if (exists == null) return null;

        // Build dynamic UPDATE
        var setClauses = new List<string> { "updated_at = $updatedAt" };
        var parameters = new List<SqliteParameter>
        {
            new("$id", id.ToString()),
            new("$userId", userId),
            new("$updatedAt", DateTime.UtcNow.ToString("o"))
        };

        if (repoOwner != null)
        {
            setClauses.Add("repo_owner = $repoOwner");
            parameters.Add(new SqliteParameter("$repoOwner", repoOwner));
        }

        if (repoName != null)
        {
            setClauses.Add("repo_name = $repoName");
            parameters.Add(new SqliteParameter("$repoName", repoName));
        }

        if (connectionId.HasValue)
        {
            setClauses.Add("connection_id = $connectionId");
            parameters.Add(new SqliteParameter("$connectionId", connectionId.Value.ToString()));
        }

        if (isActive.HasValue)
        {
            setClauses.Add("is_active = $isActive");
            parameters.Add(new SqliteParameter("$isActive", isActive.Value ? 1 : 0));
        }

        var sql = $"UPDATE vcs_sources SET {string.Join(", ", setClauses)} WHERE id = $id AND user_id = $userId";
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parameters) cmd.Parameters.Add(p);
        await cmd.ExecuteNonQueryAsync();

        // Fetch the updated record
        await using var fetchCmd = connection.CreateCommand();
        fetchCmd.CommandText = @"SELECT id, user_id, namespace, name, provider, repo_owner, repo_name, connection_id, is_active,
                                        tag_pattern, last_published_version, last_sync_status, last_sync_at, last_sync_error,
                                        created_at, updated_at
                                 FROM vcs_sources
                                 WHERE id = $id";
        fetchCmd.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await fetchCmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return MapVcsSource(reader);
    }

    public async Task<bool> DeleteVcsSourceAsync(Guid id, string userId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM vcs_sources WHERE id = $id AND user_id = $userId";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.Parameters.AddWithValue("$userId", userId);

        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<VcsSource?> GetByRepoAsync(string repoOwner, string repoName)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT id, user_id, namespace, name, provider, repo_owner, repo_name, connection_id, is_active,
                                   tag_pattern, last_published_version, last_sync_status, last_sync_at, last_sync_error,
                                   created_at, updated_at
                            FROM vcs_sources
                            WHERE repo_owner = $repoOwner AND repo_name = $repoName AND is_active = 1
                            LIMIT 1";
        cmd.Parameters.AddWithValue("$repoOwner", repoOwner);
        cmd.Parameters.AddWithValue("$repoName", repoName);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return MapVcsSource(reader);
    }

    public async Task<VcsSource?> GetAsync(Guid id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT id, user_id, namespace, name, provider, repo_owner, repo_name, connection_id, is_active,
                                   tag_pattern, last_published_version, last_sync_status, last_sync_at, last_sync_error,
                                   created_at, updated_at
                            FROM vcs_sources
                            WHERE id = $id
                            LIMIT 1";
        cmd.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return MapVcsSource(reader);
    }

    public async Task<VcsSource?> GetByModuleAsync(string @namespace, string name, string provider)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT id, user_id, namespace, name, provider, repo_owner, repo_name, connection_id, is_active,
                                   tag_pattern, last_published_version, last_sync_status, last_sync_at, last_sync_error,
                                   created_at, updated_at
                            FROM vcs_sources
                            WHERE namespace = $namespace AND name = $name AND provider = $provider AND is_active = 1
                            LIMIT 1";
        cmd.Parameters.AddWithValue("$namespace", @namespace);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$provider", provider);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return MapVcsSource(reader);
    }

    public async Task<VcsSource?> UpdateSyncStateAsync(Guid id, string status, string? lastPublishedVersion, string? error)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"UPDATE vcs_sources
                            SET last_sync_status = $status,
                                last_published_version = $lastPublishedVersion,
                                last_sync_error = $error,
                                last_sync_at = $lastSyncAt,
                                updated_at = $updatedAt
                            WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$lastPublishedVersion", (object?)lastPublishedVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lastSyncAt", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("o"));

        var rows = await cmd.ExecuteNonQueryAsync();
        if (rows == 0) return null;

        return await GetAsync(id);
    }

    private static VcsSource MapVcsSource(SqliteDataReader reader)
    {
        return new VcsSource
        {
            Id = Guid.Parse(reader.GetString(0)),
            UserId = reader.GetString(1),
            Namespace = reader.GetString(2),
            Name = reader.GetString(3),
            Provider = reader.GetString(4),
            RepoOwner = reader.GetString(5),
            RepoName = reader.GetString(6),
            ConnectionId = Guid.Parse(reader.GetString(7)),
            IsActive = reader.GetInt32(8) == 1,
            TagPattern = reader.GetString(9),
            LastPublishedVersion = reader.IsDBNull(10) ? null : reader.GetString(10),
            LastSyncStatus = reader.GetString(11),
            LastSyncAt = reader.IsDBNull(12) ? null : DateTime.Parse(reader.GetString(12)),
            LastSyncError = reader.IsDBNull(13) ? null : reader.GetString(13),
            CreatedAt = DateTime.Parse(reader.GetString(14)),
            UpdatedAt = DateTime.Parse(reader.GetString(15))
        };
    }
}
