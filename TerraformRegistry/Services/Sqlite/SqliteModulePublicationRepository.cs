using System.Globalization;
using Microsoft.Data.Sqlite;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services.Sqlite;

public sealed class SqliteModulePublicationRepository(string connectionString) : IModulePublicationRepository
{
    public async Task CreatePublicationAttemptWithExtractionJobAsync(
        ModulePublicationAttempt attempt,
        ModuleExtractionJob job)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO module_publication_attempts (
                    id, namespace, name, provider, version, state, staging_key,
                    expected_revision, committed_revision, error, created_at, updated_at, completed_at)
                VALUES (
                    $id, $namespace, $name, $provider, $version, $state, $stagingKey,
                    $expectedRevision, $committedRevision, $error, $createdAt, $updatedAt, $completedAt)
                """;
            AddAttemptParameters(command, attempt);
            await command.ExecuteNonQueryAsync();
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO module_extraction_jobs (
                    id, publication_attempt_id, namespace, name, provider, version, state, created_at, updated_at)
                VALUES ($id, $attemptId, $namespace, $name, $provider, $version, $state, $createdAt, $updatedAt)
                """;
            AddJobParameters(command, job);
            await command.ExecuteNonQueryAsync();
        }

        transaction.Commit();
    }

    public async Task<ModulePublicationAttempt?> GetPublicationAttemptAsync(Guid id)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, namespace, name, provider, version, state, staging_key,
                   expected_revision, committed_revision, error, created_at, updated_at, completed_at
            FROM module_publication_attempts
            WHERE id = $id
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync() ? ReadAttempt(reader) : null;
    }

    public async Task<ModuleExtractionJob?> GetExtractionJobAsync(Guid id)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, publication_attempt_id, namespace, name, provider, version, state, created_at, updated_at
            FROM module_extraction_jobs
            WHERE id = $id
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync() ? ReadJob(reader) : null;
    }

    private static void AddAttemptParameters(SqliteCommand command, ModulePublicationAttempt attempt)
    {
        command.Parameters.AddWithValue("$id", attempt.Id.ToString());
        command.Parameters.AddWithValue("$namespace", attempt.Namespace);
        command.Parameters.AddWithValue("$name", attempt.Name);
        command.Parameters.AddWithValue("$provider", attempt.Provider);
        command.Parameters.AddWithValue("$version", attempt.Version);
        command.Parameters.AddWithValue("$state", attempt.State);
        command.Parameters.AddWithValue("$stagingKey", attempt.StagingKey);
        command.Parameters.AddWithValue("$expectedRevision", (object?)attempt.ExpectedRevision ?? DBNull.Value);
        command.Parameters.AddWithValue("$committedRevision", (object?)attempt.CommittedRevision ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)attempt.Error ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", attempt.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", attempt.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$completedAt", attempt.CompletedAt?.ToString("O") ?? (object)DBNull.Value);
    }

    private static void AddJobParameters(SqliteCommand command, ModuleExtractionJob job)
    {
        command.Parameters.AddWithValue("$id", job.Id.ToString());
        command.Parameters.AddWithValue("$attemptId", job.PublicationAttemptId.ToString());
        command.Parameters.AddWithValue("$namespace", job.Namespace);
        command.Parameters.AddWithValue("$name", job.Name);
        command.Parameters.AddWithValue("$provider", job.Provider);
        command.Parameters.AddWithValue("$version", job.Version);
        command.Parameters.AddWithValue("$state", job.State);
        command.Parameters.AddWithValue("$createdAt", job.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", job.UpdatedAt.ToString("O"));
    }

    private static ModulePublicationAttempt ReadAttempt(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(reader.GetString(0)),
        Namespace = reader.GetString(1),
        Name = reader.GetString(2),
        Provider = reader.GetString(3),
        Version = reader.GetString(4),
        State = reader.GetString(5),
        StagingKey = reader.GetString(6),
        ExpectedRevision = reader.IsDBNull(7) ? null : reader.GetString(7),
        CommittedRevision = reader.IsDBNull(8) ? null : reader.GetString(8),
        Error = reader.IsDBNull(9) ? null : reader.GetString(9),
        CreatedAt = DateTime.Parse(reader.GetString(10), null, DateTimeStyles.RoundtripKind),
        UpdatedAt = DateTime.Parse(reader.GetString(11), null, DateTimeStyles.RoundtripKind),
        CompletedAt = reader.IsDBNull(12)
            ? null
            : DateTime.Parse(reader.GetString(12), null, DateTimeStyles.RoundtripKind)
    };

    private static ModuleExtractionJob ReadJob(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(reader.GetString(0)),
        PublicationAttemptId = Guid.Parse(reader.GetString(1)),
        Namespace = reader.GetString(2),
        Name = reader.GetString(3),
        Provider = reader.GetString(4),
        Version = reader.GetString(5),
        State = reader.GetString(6),
        CreatedAt = DateTime.Parse(reader.GetString(7), null, DateTimeStyles.RoundtripKind),
        UpdatedAt = DateTime.Parse(reader.GetString(8), null, DateTimeStyles.RoundtripKind)
    };
}
