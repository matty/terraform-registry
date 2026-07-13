using Npgsql;
using NpgsqlTypes;
using System.Text.Json;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.PostgreSQL.Repositories;

public sealed class PostgreSqlModulePublicationRepository(string connectionString) :
    IModulePublicationRepository,
    IModuleExtractionJobRepository
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
                id, publication_attempt_id, namespace, name, provider, version, state, owner_id,
                lease_expires_at, attempt_count, last_error, created_at, updated_at, completed_at)
            VALUES (
                @jobId, @attemptId, @jobNamespace, @jobName, @jobProvider, @jobVersion, @jobState,
                @jobOwnerId, @jobLeaseExpiresAt, @jobAttemptCount, @jobLastError,
                @jobCreatedAt, @jobUpdatedAt, @jobCompletedAt);
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

    public async Task<bool> TryFailStagedPublicationAsync(Guid attemptId, string failureReason)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            UPDATE module_publication_attempts
            SET state = @failed, error = @failureReason, updated_at = @updatedAt, completed_at = @completedAt
            WHERE id = @id AND state = @staged
            """,
            connection);
        command.Parameters.AddWithValue("@failed", ModulePublicationAttemptState.Failed);
        command.Parameters.AddWithValue("@failureReason", failureReason);
        command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow);
        command.Parameters.AddWithValue("@completedAt", DateTime.UtcNow);
        command.Parameters.AddWithValue("@id", attemptId);
        command.Parameters.AddWithValue("@staged", ModulePublicationAttemptState.Staged);
        return await command.ExecuteNonQueryAsync() == 1;
    }

    public async Task<bool> TryCommitStagedPublicationAsync(
        ModulePublicationAttempt attempt,
        ModuleStorage newModule,
        ModuleStorage? expectedModule)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var catalogChanged = expectedModule is null
            ? await TryInsertCatalogAsync(connection, transaction, newModule)
            : await TryReplaceCatalogAsync(connection, transaction, expectedModule, newModule);
        if (!catalogChanged)
            return false;

        await using var attemptCommand = new NpgsqlCommand(
            """
            UPDATE module_publication_attempts
            SET state = @committed, updated_at = @updatedAt, completed_at = @completedAt, error = NULL
            WHERE id = @id
              AND namespace = @namespace
              AND name = @name
              AND provider = @provider
              AND version = @version
              AND state = @staged
            """,
            connection,
            transaction);
        attemptCommand.Parameters.AddWithValue("@committed", ModulePublicationAttemptState.Committed);
        attemptCommand.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow);
        attemptCommand.Parameters.AddWithValue("@completedAt", DateTime.UtcNow);
        attemptCommand.Parameters.AddWithValue("@id", attempt.Id);
        attemptCommand.Parameters.AddWithValue("@namespace", newModule.Namespace);
        attemptCommand.Parameters.AddWithValue("@name", newModule.Name);
        attemptCommand.Parameters.AddWithValue("@provider", newModule.Provider);
        attemptCommand.Parameters.AddWithValue("@version", newModule.Version);
        attemptCommand.Parameters.AddWithValue("@staged", ModulePublicationAttemptState.Staged);
        if (await attemptCommand.ExecuteNonQueryAsync() != 1)
            return false;

        await using var jobCommand = new NpgsqlCommand(
            """
            UPDATE module_extraction_jobs
            SET state = @pending, updated_at = @updatedAt
            WHERE publication_attempt_id = @attemptId AND state = @staged
            """,
            connection,
            transaction);
        jobCommand.Parameters.AddWithValue("@pending", ModuleExtractionJobState.Pending);
        jobCommand.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow);
        jobCommand.Parameters.AddWithValue("@attemptId", attempt.Id);
        jobCommand.Parameters.AddWithValue("@staged", ModuleExtractionJobState.Staged);
        if (await jobCommand.ExecuteNonQueryAsync() != 1)
            return false;

        await transaction.CommitAsync();
        return true;
    }

    public async Task<ModuleExtractionJob?> GetExtractionJobAsync(Guid id)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT id, publication_attempt_id, namespace, name, provider, version, state, owner_id,
                   lease_expires_at, attempt_count, last_error, created_at, updated_at, completed_at
            FROM module_extraction_jobs
            WHERE id = @id
            """,
            connection);
        command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync() ? ReadJob(reader) : null;
    }

    public async Task<ModuleExtractionJob?> TryClaimNextExtractionJobAsync(
        string ownerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            WITH candidate AS (
                SELECT id
                FROM module_extraction_jobs
                WHERE state IN (@pending, @retry)
                   OR (state = @processing AND lease_expires_at <= @now)
                ORDER BY created_at, id
                FOR UPDATE SKIP LOCKED
                LIMIT 1)
            UPDATE module_extraction_jobs AS jobs
            SET state = @processing, owner_id = @ownerId, lease_expires_at = @leaseExpiresAt,
                attempt_count = jobs.attempt_count + 1, updated_at = @updatedAt,
                last_error = CASE WHEN jobs.state = @processing THEN jobs.last_error ELSE NULL END
            FROM candidate
            WHERE jobs.id = candidate.id
            RETURNING jobs.id, jobs.publication_attempt_id, jobs.namespace, jobs.name, jobs.provider, jobs.version,
                      jobs.state, jobs.owner_id, jobs.lease_expires_at, jobs.attempt_count, jobs.last_error,
                      jobs.created_at, jobs.updated_at, jobs.completed_at
            """, connection);
        AddClaimParameters(command, ownerId, leaseDuration, now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadJob(reader) : null;
    }

    public Task<bool> TryHeartbeatExtractionJobAsync(Guid jobId, string ownerId, TimeSpan leaseDuration,
        CancellationToken cancellationToken = default) =>
        UpdateLeasedJobAsync(jobId, ownerId, """
            UPDATE module_extraction_jobs
            SET lease_expires_at = @leaseExpiresAt, updated_at = @updatedAt
            WHERE id = @id AND state = @processing AND owner_id = @ownerId
            """, leaseDuration, cancellationToken);

    public Task<bool> TryCompleteExtractionJobAsync(Guid jobId, string ownerId,
        CancellationToken cancellationToken = default) =>
        UpdateLeasedJobAsync(jobId, ownerId, """
            UPDATE module_extraction_jobs
            SET state = @succeeded, owner_id = NULL, lease_expires_at = NULL,
                updated_at = @updatedAt, completed_at = @completedAt, last_error = NULL
            WHERE id = @id AND state = @processing AND owner_id = @ownerId
            """, TimeSpan.Zero, cancellationToken);

    public async Task<bool> TryFailExtractionJobAsync(Guid jobId, string ownerId, string failureReason,
        int maximumAttempts, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            UPDATE module_extraction_jobs
            SET state = CASE WHEN attempt_count >= @maximumAttempts THEN @deadLetter ELSE @retry END,
                owner_id = NULL, lease_expires_at = NULL, last_error = @failureReason,
                updated_at = @updatedAt,
                completed_at = CASE WHEN attempt_count >= @maximumAttempts THEN @completedAt ELSE NULL END
            WHERE id = @id AND state = @processing AND owner_id = @ownerId
            """, connection);
        command.Parameters.AddWithValue("@maximumAttempts", maximumAttempts);
        command.Parameters.AddWithValue("@deadLetter", ModuleExtractionJobState.DeadLetter);
        command.Parameters.AddWithValue("@retry", ModuleExtractionJobState.Retry);
        command.Parameters.AddWithValue("@failureReason", failureReason);
        command.Parameters.AddWithValue("@updatedAt", now);
        command.Parameters.AddWithValue("@completedAt", now);
        command.Parameters.AddWithValue("@id", jobId);
        command.Parameters.AddWithValue("@processing", ModuleExtractionJobState.Processing);
        command.Parameters.AddWithValue("@ownerId", ownerId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<int> CountPendingExtractionJobsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM module_extraction_jobs WHERE state IN (@pending, @retry)", connection);
        command.Parameters.AddWithValue("@pending", ModuleExtractionJobState.Pending);
        command.Parameters.AddWithValue("@retry", ModuleExtractionJobState.Retry);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
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

    private static async Task<bool> TryInsertCatalogAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ModuleStorage module)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO modules (
                namespace, name, provider, version, description, storage_path, published_at, dependencies, metadata)
            VALUES (
                @namespace, @name, @provider, @version, @description, @storagePath, @publishedAt, @dependencies, @metadata)
            ON CONFLICT(namespace, name, provider, version) DO NOTHING
            """,
            connection,
            transaction);
        AddModuleParameters(command, module, string.Empty);
        return await command.ExecuteNonQueryAsync() == 1;
    }

    private static async Task<bool> TryReplaceCatalogAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ModuleStorage expected,
        ModuleStorage replacement)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE modules
            SET description = @newDescription,
                storage_path = @newStoragePath,
                published_at = @newPublishedAt,
                dependencies = @newDependencies,
                metadata = @newMetadata
            WHERE namespace = @namespace
              AND name = @name
              AND provider = @provider
              AND version = @version
              AND description = @description
              AND storage_path = @storagePath
              AND published_at = @publishedAt
              AND dependencies = @dependencies
              AND metadata = @metadata
              AND deleted_at IS NULL
            """,
            connection,
            transaction);
        AddModuleParameters(command, expected, string.Empty);
        AddModuleParameters(command, replacement, "new");
        return await command.ExecuteNonQueryAsync() == 1;
    }

    private static void AddModuleParameters(NpgsqlCommand command, ModuleStorage module, string prefix)
    {
        command.Parameters.AddWithValue(ParameterName(prefix, "namespace"), module.Namespace);
        command.Parameters.AddWithValue(ParameterName(prefix, "name"), module.Name);
        command.Parameters.AddWithValue(ParameterName(prefix, "provider"), module.Provider);
        command.Parameters.AddWithValue(ParameterName(prefix, "version"), module.Version);
        command.Parameters.AddWithValue(ParameterName(prefix, "description"), module.Description);
        command.Parameters.AddWithValue(ParameterName(prefix, "storagePath"), module.FilePath);
        command.Parameters.AddWithValue(ParameterName(prefix, "publishedAt"), module.PublishedAt);
        command.Parameters.AddWithValue(ParameterName(prefix, "dependencies"), JsonSerializer.Serialize(module.Dependencies)).NpgsqlDbType =
            NpgsqlDbType.Jsonb;
        command.Parameters.AddWithValue(ParameterName(prefix, "metadata"), JsonSerializer.Serialize(module.Metadata)).NpgsqlDbType =
            NpgsqlDbType.Jsonb;
    }

    private static string ParameterName(string prefix, string name) =>
        "@" + (string.IsNullOrEmpty(prefix) ? name : prefix + char.ToUpperInvariant(name[0]) + name[1..]);

    private static void AddJobParameters(NpgsqlCommand command, ModuleExtractionJob job)
    {
        command.Parameters.AddWithValue("@jobId", job.Id);
        command.Parameters.AddWithValue("@attemptId", job.PublicationAttemptId);
        command.Parameters.AddWithValue("@jobNamespace", job.Namespace);
        command.Parameters.AddWithValue("@jobName", job.Name);
        command.Parameters.AddWithValue("@jobProvider", job.Provider);
        command.Parameters.AddWithValue("@jobVersion", job.Version);
        command.Parameters.AddWithValue("@jobState", job.State);
        command.Parameters.AddWithValue("@jobOwnerId", (object?)job.OwnerId ?? DBNull.Value);
        command.Parameters.AddWithValue("@jobLeaseExpiresAt", (object?)job.LeaseExpiresAt ?? DBNull.Value);
        command.Parameters.AddWithValue("@jobAttemptCount", job.AttemptCount);
        command.Parameters.AddWithValue("@jobLastError", (object?)job.LastError ?? DBNull.Value);
        command.Parameters.AddWithValue("@jobCreatedAt", job.CreatedAt);
        command.Parameters.AddWithValue("@jobUpdatedAt", job.UpdatedAt);
        command.Parameters.AddWithValue("@jobCompletedAt", (object?)job.CompletedAt ?? DBNull.Value);
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
        OwnerId = reader.IsDBNull(7) ? null : reader.GetString(7),
        LeaseExpiresAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
        AttemptCount = reader.GetInt32(9),
        LastError = reader.IsDBNull(10) ? null : reader.GetString(10),
        CreatedAt = reader.GetDateTime(11),
        UpdatedAt = reader.GetDateTime(12),
        CompletedAt = reader.IsDBNull(13) ? null : reader.GetDateTime(13)
    };

    private static void AddClaimParameters(NpgsqlCommand command, string ownerId, TimeSpan leaseDuration, DateTime now)
    {
        command.Parameters.AddWithValue("@pending", ModuleExtractionJobState.Pending);
        command.Parameters.AddWithValue("@retry", ModuleExtractionJobState.Retry);
        command.Parameters.AddWithValue("@processing", ModuleExtractionJobState.Processing);
        command.Parameters.AddWithValue("@now", now);
        command.Parameters.AddWithValue("@ownerId", ownerId);
        command.Parameters.AddWithValue("@leaseExpiresAt", now.Add(leaseDuration));
        command.Parameters.AddWithValue("@updatedAt", now);
    }

    private async Task<bool> UpdateLeasedJobAsync(Guid jobId, string ownerId, string sql, TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", jobId);
        command.Parameters.AddWithValue("@ownerId", ownerId);
        command.Parameters.AddWithValue("@processing", ModuleExtractionJobState.Processing);
        command.Parameters.AddWithValue("@succeeded", ModuleExtractionJobState.Succeeded);
        command.Parameters.AddWithValue("@leaseExpiresAt", now.Add(leaseDuration));
        command.Parameters.AddWithValue("@updatedAt", now);
        command.Parameters.AddWithValue("@completedAt", now);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
