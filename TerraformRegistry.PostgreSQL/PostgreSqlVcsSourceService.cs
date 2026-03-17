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

        var sql = "SELECT id, user_id, namespace, name, provider, repo_owner, repo_name, pat_encrypted, webhook_secret, is_active, created_at, updated_at FROM vcs_sources WHERE user_id = @userId ORDER BY created_at DESC";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@userId", userId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            sources.Add(MapVcsSource(reader));
        }

        return sources;
    }

    public async Task<VcsSource> CreateVcsSourceAsync(string userId, string @namespace, string name, string provider, string repoOwner, string repoName, string? patEncrypted, string webhookSecret)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
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
            PatEncrypted = patEncrypted,
            WebhookSecret = webhookSecret,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var sql = @"INSERT INTO vcs_sources (id, user_id, namespace, name, provider, repo_owner, repo_name, pat_encrypted, webhook_secret, is_active, created_at, updated_at)
                    VALUES (@id, @userId, @namespace, @name, @provider, @repoOwner, @repoName, @patEncrypted, @webhookSecret, @isActive, @createdAt, @updatedAt)";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", vcsSource.Id);
        cmd.Parameters.AddWithValue("@userId", vcsSource.UserId);
        cmd.Parameters.AddWithValue("@namespace", vcsSource.Namespace);
        cmd.Parameters.AddWithValue("@name", vcsSource.Name);
        cmd.Parameters.AddWithValue("@provider", vcsSource.Provider);
        cmd.Parameters.AddWithValue("@repoOwner", vcsSource.RepoOwner);
        cmd.Parameters.AddWithValue("@repoName", vcsSource.RepoName);
        cmd.Parameters.AddWithValue("@patEncrypted", (object?)vcsSource.PatEncrypted ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@webhookSecret", vcsSource.WebhookSecret);
        cmd.Parameters.AddWithValue("@isActive", vcsSource.IsActive);
        cmd.Parameters.AddWithValue("@createdAt", vcsSource.CreatedAt);
        cmd.Parameters.AddWithValue("@updatedAt", vcsSource.UpdatedAt);

        await cmd.ExecuteNonQueryAsync();
        return vcsSource;
    }

    public async Task<VcsSource?> UpdateVcsSourceAsync(Guid id, string userId, string? repoOwner, string? repoName, string? patEncrypted, bool? isActive)
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

        if (patEncrypted != null)
        {
            setClauses.Add("pat_encrypted = @patEncrypted");
            parameters.Add(new NpgsqlParameter("@patEncrypted", patEncrypted));
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
        var selectSql = "SELECT id, user_id, namespace, name, provider, repo_owner, repo_name, pat_encrypted, webhook_secret, is_active, created_at, updated_at FROM vcs_sources WHERE id = @selectId AND user_id = @selectUserId";
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

        var sql = "SELECT id, user_id, namespace, name, provider, repo_owner, repo_name, pat_encrypted, webhook_secret, is_active, created_at, updated_at FROM vcs_sources WHERE repo_owner = @repoOwner AND repo_name = @repoName AND is_active = true LIMIT 1";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@repoOwner", repoOwner);
        cmd.Parameters.AddWithValue("@repoName", repoName);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return MapVcsSource(reader);
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
            PatEncrypted = reader.IsDBNull(7) ? null : reader.GetString(7),
            WebhookSecret = reader.GetString(8),
            IsActive = reader.GetBoolean(9),
            CreatedAt = reader.GetDateTime(10),
            UpdatedAt = reader.GetDateTime(11)
        };
    }
}
