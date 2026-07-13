using Npgsql;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.PostgreSQL.Repositories;

public sealed class PostgreSqlModulePublicationRepository(string connectionString) : IModulePublicationRepository
{
    public async Task CreatePublicationAttemptWithExtractionJobAsync(ModulePublicationAttempt attempt, ModuleExtractionJob job)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(@"INSERT INTO module_publication_attempts (id, namespace, name, provider, version, state, staging_key, created_at, updated_at) VALUES (@id,@ns,@name,@provider,@version,@state,@key,@created,@updated); INSERT INTO module_extraction_jobs (id, publication_attempt_id, namespace, name, provider, version, state, created_at, updated_at) VALUES (@jobId,@attemptId,@jobNs,@jobName,@jobProvider,@jobVersion,@jobState,@jobCreated,@jobUpdated);", connection, transaction);
        command.Parameters.AddWithValue("@id", attempt.Id); command.Parameters.AddWithValue("@ns", attempt.Namespace); command.Parameters.AddWithValue("@name", attempt.Name); command.Parameters.AddWithValue("@provider", attempt.Provider); command.Parameters.AddWithValue("@version", attempt.Version); command.Parameters.AddWithValue("@state", attempt.State); command.Parameters.AddWithValue("@key", attempt.StagingKey); command.Parameters.AddWithValue("@created", attempt.CreatedAt); command.Parameters.AddWithValue("@updated", attempt.UpdatedAt);
        command.Parameters.AddWithValue("@jobId", job.Id); command.Parameters.AddWithValue("@attemptId", job.PublicationAttemptId); command.Parameters.AddWithValue("@jobNs", job.Namespace); command.Parameters.AddWithValue("@jobName", job.Name); command.Parameters.AddWithValue("@jobProvider", job.Provider); command.Parameters.AddWithValue("@jobVersion", job.Version); command.Parameters.AddWithValue("@jobState", job.State); command.Parameters.AddWithValue("@jobCreated", job.CreatedAt); command.Parameters.AddWithValue("@jobUpdated", job.UpdatedAt);
        await command.ExecuteNonQueryAsync(); await transaction.CommitAsync();
    }
    public async Task<ModulePublicationAttempt?> GetPublicationAttemptAsync(Guid id) { await using var c=new NpgsqlConnection(connectionString); await c.OpenAsync(); await using var q=new NpgsqlCommand("SELECT id,namespace,name,provider,version,state,staging_key,created_at,updated_at FROM module_publication_attempts WHERE id=@id",c); q.Parameters.AddWithValue("@id",id); await using var r=await q.ExecuteReaderAsync(); return await r.ReadAsync()?new ModulePublicationAttempt{Id=r.GetGuid(0),Namespace=r.GetString(1),Name=r.GetString(2),Provider=r.GetString(3),Version=r.GetString(4),State=r.GetString(5),StagingKey=r.GetString(6),CreatedAt=r.GetDateTime(7),UpdatedAt=r.GetDateTime(8)}:null; }
    public async Task<ModuleExtractionJob?> GetExtractionJobAsync(Guid id) { await using var c=new NpgsqlConnection(connectionString); await c.OpenAsync(); await using var q=new NpgsqlCommand("SELECT id,publication_attempt_id,namespace,name,provider,version,state,created_at,updated_at FROM module_extraction_jobs WHERE id=@id",c); q.Parameters.AddWithValue("@id",id); await using var r=await q.ExecuteReaderAsync(); return await r.ReadAsync()?new ModuleExtractionJob{Id=r.GetGuid(0),PublicationAttemptId=r.GetGuid(1),Namespace=r.GetString(2),Name=r.GetString(3),Provider=r.GetString(4),Version=r.GetString(5),State=r.GetString(6),CreatedAt=r.GetDateTime(7),UpdatedAt=r.GetDateTime(8)}:null; }
}
