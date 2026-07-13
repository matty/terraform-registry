using Microsoft.Data.Sqlite;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services.Sqlite;

public sealed class SqliteModulePublicationRepository(string connectionString) : IModulePublicationRepository
{
    public async Task CreatePublicationAttemptWithExtractionJobAsync(ModulePublicationAttempt attempt, ModuleExtractionJob job)
    {
        await using var connection = new SqliteConnection(connectionString); await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO module_publication_attempts (id, namespace, name, provider, version, state, staging_key, created_at, updated_at) VALUES ($id,$ns,$name,$provider,$version,$state,$key,$created,$updated)";
            command.Parameters.AddWithValue("$id", attempt.Id.ToString()); command.Parameters.AddWithValue("$ns", attempt.Namespace); command.Parameters.AddWithValue("$name", attempt.Name); command.Parameters.AddWithValue("$provider", attempt.Provider); command.Parameters.AddWithValue("$version", attempt.Version); command.Parameters.AddWithValue("$state", attempt.State); command.Parameters.AddWithValue("$key", attempt.StagingKey); command.Parameters.AddWithValue("$created", attempt.CreatedAt.ToString("O")); command.Parameters.AddWithValue("$updated", attempt.UpdatedAt.ToString("O"));
            await command.ExecuteNonQueryAsync();
        }
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO module_extraction_jobs (id, publication_attempt_id, namespace, name, provider, version, state, created_at, updated_at) VALUES ($id,$attempt,$ns,$name,$provider,$version,$state,$created,$updated)";
            command.Parameters.AddWithValue("$id", job.Id.ToString()); command.Parameters.AddWithValue("$attempt", job.PublicationAttemptId.ToString()); command.Parameters.AddWithValue("$ns", job.Namespace); command.Parameters.AddWithValue("$name", job.Name); command.Parameters.AddWithValue("$provider", job.Provider); command.Parameters.AddWithValue("$version", job.Version); command.Parameters.AddWithValue("$state", job.State); command.Parameters.AddWithValue("$created", job.CreatedAt.ToString("O")); command.Parameters.AddWithValue("$updated", job.UpdatedAt.ToString("O"));
            await command.ExecuteNonQueryAsync();
        }
        transaction.Commit();
    }
    public Task<ModulePublicationAttempt?> GetPublicationAttemptAsync(Guid id) => GetAttemptAsync(id);
    private async Task<ModulePublicationAttempt?> GetAttemptAsync(Guid id) { await using var c=new SqliteConnection(connectionString); await c.OpenAsync(); await using var q=c.CreateCommand(); q.CommandText="SELECT id,namespace,name,provider,version,state,staging_key,created_at,updated_at FROM module_publication_attempts WHERE id=$id"; q.Parameters.AddWithValue("$id",id.ToString()); await using var r=await q.ExecuteReaderAsync(); return await r.ReadAsync()?new ModulePublicationAttempt{Id=Guid.Parse(r.GetString(0)),Namespace=r.GetString(1),Name=r.GetString(2),Provider=r.GetString(3),Version=r.GetString(4),State=r.GetString(5),StagingKey=r.GetString(6),CreatedAt=DateTime.Parse(r.GetString(7), null, System.Globalization.DateTimeStyles.RoundtripKind),UpdatedAt=DateTime.Parse(r.GetString(8), null, System.Globalization.DateTimeStyles.RoundtripKind)}:null; }
    public async Task<ModuleExtractionJob?> GetExtractionJobAsync(Guid id) { await using var c=new SqliteConnection(connectionString); await c.OpenAsync(); await using var q=c.CreateCommand(); q.CommandText="SELECT id,publication_attempt_id,namespace,name,provider,version,state,created_at,updated_at FROM module_extraction_jobs WHERE id=$id"; q.Parameters.AddWithValue("$id",id.ToString()); await using var r=await q.ExecuteReaderAsync(); return await r.ReadAsync()?new ModuleExtractionJob{Id=Guid.Parse(r.GetString(0)),PublicationAttemptId=Guid.Parse(r.GetString(1)),Namespace=r.GetString(2),Name=r.GetString(3),Provider=r.GetString(4),Version=r.GetString(5),State=r.GetString(6),CreatedAt=DateTime.Parse(r.GetString(7), null, System.Globalization.DateTimeStyles.RoundtripKind),UpdatedAt=DateTime.Parse(r.GetString(8), null, System.Globalization.DateTimeStyles.RoundtripKind)}:null; }
}
