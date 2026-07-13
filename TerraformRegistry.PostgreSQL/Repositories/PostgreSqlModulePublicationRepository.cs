using Npgsql;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.PostgreSQL.Repositories;

public sealed class PostgreSqlModulePublicationRepository(string connectionString) : IModulePublicationRepository
{
    public async Task CreatePublicationAttemptWithExtractionJobAsync(
        ModulePublicationAttempt attempt,
        ModuleExtractionJob job)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO module_publication_attempts (
                id, namespace, name, provider, version, state, staging_key,
                expected_revision, committed_revision, error, created_at, updated_at, completed_at)
            VALUES (
                @id, @namespace, @name, @provider, @version, @state, @stagingKey,
                @expectedRevision, @committedRevision, @error, @createdAt, @updatedAt, @completedAt);

            INSERT INTO module_extraction_jobs (
                id, publication_attempt_id, namespace, name, provider, version, state, created_at, updated_at)
            VALUES (
                @jobId, @attemptId, @jobNamespace, @jobName, @jobProvider, @jobVersion, @jobState,
                @jobCreatedAt, @jobUpdatedAt);
            """,
            connection,
            transaction);
        AddAttemptParameters(command, attempt);
        AddJobParameters(command, job);

        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    public async Task<ModulePublicationAttempt?> GetPublicationAttemptAsync(Guid id)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT id, namespace, name, provider, version, state, staging_key,
                   expected_revision, committed_revision, error, created_at, updated_at, completed_at
            FROM module_publication_attempts
            WHERE id = @id
            """,
            connection);
        command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync() ? ReadAttempt(reader) : null;
    }

    public async Task<ModuleExtractionJob?> GetExtractionJobAsync(Guid id)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT id, publication_attempt_id, namespace, name, provider, version, state, created_at, updated_at
            FROM module_extraction_jobs
            WHERE id = @id
            """,
            connection);
        command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync() ? ReadJob(reader) : null;
    }

    private static void AddAttemptParameters(NpgsqlCommand command, ModulePublicationAttempt attempt)
    {
        command.Parameters.AddWithValue("@id", attempt.Id);
        command.Parameters.AddWithValue("@namespace", attempt.Namespace);
        command.Parameters.AddWithValue("@name", attempt.Name);
        command.Parameters.AddWithValue("@provider", attempt.Provider);
        command.Parameters.AddWithValue("@version", attempt.Version);
        command.Parameters.AddWithValue("@state", attempt.State);
        command.Parameters.AddWithValue("@stagingKey", attempt.StagingKey);
        command.Parameters.AddWithValue("@expectedRevision", (object?)attempt.ExpectedRevision ?? DBNull.Value);
        command.Parameters.AddWithValue("@committedRevision", (object?)attempt.CommittedRevision ?? DBNull.Value);
        command.Parameters.AddWithValue("@error", (object?)attempt.Error ?? DBNull.Value);
        command.Parameters.AddWithValue("@createdAt", attempt.CreatedAt);
        command.Parameters.AddWithValue("@updatedAt", attempt.UpdatedAt);
        command.Parameters.AddWithValue("@completedAt", (object?)attempt.CompletedAt ?? DBNull.Value);
    }

    private static void AddJobParameters(NpgsqlCommand command, ModuleExtractionJob job)
    {
        command.Parameters.AddWithValue("@jobId", job.Id);
        command.Parameters.AddWithValue("@attemptId", job.PublicationAttemptId);
        command.Parameters.AddWithValue("@jobNamespace", job.Namespace);
        command.Parameters.AddWithValue("@jobName", job.Name);
        command.Parameters.AddWithValue("@jobProvider", job.Provider);
        command.Parameters.AddWithValue("@jobVersion", job.Version);
        command.Parameters.AddWithValue("@jobState", job.State);
        command.Parameters.AddWithValue("@jobCreatedAt", job.CreatedAt);
        command.Parameters.AddWithValue("@jobUpdatedAt", job.UpdatedAt);
    }

    private static ModulePublicationAttempt ReadAttempt(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        Namespace = reader.GetString(1),
        Name = reader.GetString(2),
        Provider = reader.GetString(3),
        Version = reader.GetString(4),
        State = reader.GetString(5),
        StagingKey = reader.GetString(6),
        ExpectedRevision = reader.IsDBNull(7) ? null : reader.GetString(7),
        CommittedRevision = reader.IsDBNull(8) ? null : reader.GetString(8),
        Error = reader.IsDBNull(9) ? null : reader.GetString(9),
        CreatedAt = reader.GetDateTime(10),
        UpdatedAt = reader.GetDateTime(11),
        CompletedAt = reader.IsDBNull(12) ? null : reader.GetDateTime(12)
    };

    private static ModuleExtractionJob ReadJob(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        PublicationAttemptId = reader.GetGuid(1),
        Namespace = reader.GetString(2),
        Name = reader.GetString(3),
        Provider = reader.GetString(4),
        Version = reader.GetString(5),
        State = reader.GetString(6),
        CreatedAt = reader.GetDateTime(7),
        UpdatedAt = reader.GetDateTime(8)
    };
}
