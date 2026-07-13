using System.Globalization;
using System.Text.Json;
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

    public async Task<bool> TryCommitStagedPublicationAsync(
        ModulePublicationAttempt attempt,
        ModuleStorage newModule,
        ModuleStorage? expectedModule)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        var catalogChanged = expectedModule is null
            ? await TryInsertCatalogAsync(connection, transaction, newModule)
            : await TryReplaceCatalogAsync(connection, transaction, expectedModule, newModule);
        if (!catalogChanged)
            return false;

        await using var attemptCommand = connection.CreateCommand();
        attemptCommand.Transaction = transaction;
        attemptCommand.CommandText = """
            UPDATE module_publication_attempts
            SET state = $committed, updated_at = $updatedAt, completed_at = $completedAt, error = NULL
            WHERE id = $id
              AND namespace = $namespace
              AND name = $name
              AND provider = $provider
              AND version = $version
              AND state = $staged
            """;
        attemptCommand.Parameters.AddWithValue("$committed", ModulePublicationAttemptState.Committed);
        attemptCommand.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("O"));
        attemptCommand.Parameters.AddWithValue("$completedAt", DateTime.UtcNow.ToString("O"));
        attemptCommand.Parameters.AddWithValue("$id", attempt.Id.ToString());
        attemptCommand.Parameters.AddWithValue("$namespace", newModule.Namespace);
        attemptCommand.Parameters.AddWithValue("$name", newModule.Name);
        attemptCommand.Parameters.AddWithValue("$provider", newModule.Provider);
        attemptCommand.Parameters.AddWithValue("$version", newModule.Version);
        attemptCommand.Parameters.AddWithValue("$staged", ModulePublicationAttemptState.Staged);
        if (await attemptCommand.ExecuteNonQueryAsync() != 1)
            return false;

        transaction.Commit();
        return true;
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

    private static async Task<bool> TryInsertCatalogAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ModuleStorage module)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO modules (
                namespace, name, provider, version, description, storage_path, published_at, dependencies, metadata)
            VALUES (
                $namespace, $name, $provider, $version, $description, $storagePath, $publishedAt, $dependencies, $metadata)
            ON CONFLICT(namespace, name, provider, version) DO NOTHING
            """;
        AddModuleParameters(command, module, "");
        return await command.ExecuteNonQueryAsync() == 1;
    }

    private static async Task<bool> TryReplaceCatalogAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ModuleStorage expected,
        ModuleStorage replacement)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE modules
            SET description = $newDescription,
                storage_path = $newStoragePath,
                published_at = $newPublishedAt,
                dependencies = $newDependencies,
                metadata = $newMetadata
            WHERE namespace = $namespace
              AND name = $name
              AND provider = $provider
              AND version = $version
              AND description = $description
              AND storage_path = $storagePath
              AND published_at = $publishedAt
              AND dependencies = $dependencies
              AND metadata = $metadata
              AND deleted_at IS NULL
            """;
        AddModuleParameters(command, expected, "");
        AddModuleParameters(command, replacement, "new");
        return await command.ExecuteNonQueryAsync() == 1;
    }

    private static void AddModuleParameters(SqliteCommand command, ModuleStorage module, string prefix)
    {
        command.Parameters.AddWithValue(ParameterName(prefix, "namespace"), module.Namespace);
        command.Parameters.AddWithValue(ParameterName(prefix, "name"), module.Name);
        command.Parameters.AddWithValue(ParameterName(prefix, "provider"), module.Provider);
        command.Parameters.AddWithValue(ParameterName(prefix, "version"), module.Version);
        command.Parameters.AddWithValue(ParameterName(prefix, "description"), module.Description);
        command.Parameters.AddWithValue(ParameterName(prefix, "storagePath"), module.FilePath);
        command.Parameters.AddWithValue(ParameterName(prefix, "publishedAt"), module.PublishedAt.ToString("O"));
        command.Parameters.AddWithValue(ParameterName(prefix, "dependencies"), JsonSerializer.Serialize(module.Dependencies));
        command.Parameters.AddWithValue(ParameterName(prefix, "metadata"), JsonSerializer.Serialize(module.Metadata));
    }

    private static string ParameterName(string prefix, string name) =>
        "$" + (string.IsNullOrEmpty(prefix) ? name : prefix + char.ToUpperInvariant(name[0]) + name[1..]);

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
