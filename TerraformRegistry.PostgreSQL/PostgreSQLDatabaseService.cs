using Microsoft.Extensions.Logging;
using Npgsql;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Migrations;
using TerraformRegistry.Models;
using TerraformRegistry.PostgreSQL.Repositories;

namespace TerraformRegistry.PostgreSQL;

/// <summary>
///     PostgreSQL compatibility facade for registry database storage.
/// </summary>
public class PostgreSqlDatabaseService : IDatabaseService, IModulePublicationRepository, IInitializableDb
{
    private readonly string _connectionString;
    private readonly DbUpMigrator _dbUpMigrator;
    private readonly PostgreSqlApiKeyRepository _apiKeys;
    private readonly PostgreSqlModuleDownloadRecorder _downloads;
    private readonly PostgreSqlModuleExtractionRepository _moduleExtractions;
    private readonly PostgreSqlModuleRepository _modules;
    private readonly PostgreSqlModulePublicationRepository _publications;
    private readonly PostgreSqlUserRepository _users;

    public PostgreSqlDatabaseService(string connectionString, string baseUrl, ILogger<PostgreSqlDatabaseService> logger,
        DbUpMigrator dbUpMigrator)
    {
        _connectionString = connectionString;
        _dbUpMigrator = dbUpMigrator;
        _modules = new PostgreSqlModuleRepository(connectionString, baseUrl, logger);
        _publications = new PostgreSqlModulePublicationRepository(connectionString);
        _moduleExtractions = new PostgreSqlModuleExtractionRepository(connectionString);
        _users = new PostgreSqlUserRepository(connectionString);
        _apiKeys = new PostgreSqlApiKeyRepository(connectionString);
        _downloads = new PostgreSqlModuleDownloadRecorder(connectionString, logger);
    }

    public Task<ModuleList> ListModulesAsync(ModuleSearchRequest request) =>
        _modules.ListModulesAsync(request);

    public Task CreatePublicationAttemptWithExtractionJobAsync(ModulePublicationAttempt attempt, ModuleExtractionJob job) => _publications.CreatePublicationAttemptWithExtractionJobAsync(attempt, job);
    public Task<bool> TryCommitStagedPublicationAsync(
        ModulePublicationAttempt attempt,
        ModuleStorage newModule,
        ModuleStorage? expectedModule) =>
        _publications.TryCommitStagedPublicationAsync(attempt, newModule, expectedModule);
    public Task<bool> TryFailStagedPublicationAsync(Guid attemptId, string failureReason) =>
        _publications.TryFailStagedPublicationAsync(attemptId, failureReason);
    public Task<ModulePublicationAttempt?> GetPublicationAttemptAsync(Guid id) => _publications.GetPublicationAttemptAsync(id);
    public Task<ModuleExtractionJob?> GetExtractionJobAsync(Guid id) => _publications.GetExtractionJobAsync(id);

    public Task<TerraformModule?> GetModuleAsync(string moduleNamespace, string name, string provider, string version) =>
        _modules.GetModuleAsync(moduleNamespace, name, provider, version);

    public Task<ModuleVersions> GetModuleVersionsAsync(string moduleNamespace, string name, string provider) =>
        _modules.GetModuleVersionsAsync(moduleNamespace, name, provider);

    public Task<ModuleStorage?> GetModuleStorageAsync(string moduleNamespace, string name, string provider, string version) =>
        _modules.GetModuleStorageAsync(moduleNamespace, name, provider, version);

    public Task<bool> AddModuleAsync(ModuleStorage moduleStorage) =>
        _modules.AddModuleAsync(moduleStorage);

    public Task<bool> RemoveModuleAsync(ModuleStorage moduleStorage) =>
        _modules.RemoveModuleAsync(moduleStorage);

    public Task<bool> RemoveModuleExactAsync(ModuleStorage moduleStorage) =>
        _modules.RemoveModuleExactAsync(moduleStorage);

    public Task<bool> RemoveDeletedModuleAsync(string moduleNamespace, string name, string provider, string version) =>
        _modules.RemoveDeletedModuleAsync(moduleNamespace, name, provider, version);

    public Task<bool> AddDeletedModuleAsync(ModuleStorage moduleStorage) =>
        _modules.AddDeletedModuleAsync(moduleStorage);

    public Task<bool> ReplaceModuleExactAsync(ModuleStorage existingModule, ModuleStorage newModule) =>
        _modules.ReplaceModuleExactAsync(existingModule, newModule);

    public Task<bool> SoftDeleteModuleAsync(string moduleNamespace, string name, string provider, string version) =>
        _modules.SoftDeleteModuleAsync(moduleNamespace, name, provider, version);

    public Task<bool> RestoreModuleAsync(string moduleNamespace, string name, string provider, string version) =>
        _modules.RestoreModuleAsync(moduleNamespace, name, provider, version);

    public Task<ModuleList> ListDeletedModulesAsync(ModuleSearchRequest request) =>
        _modules.ListDeletedModulesAsync(request);

    public Task<ModuleStorage?> GetModuleStorageIncludingDeletedAsync(
        string moduleNamespace,
        string name,
        string provider,
        string version) =>
        _modules.GetModuleStorageIncludingDeletedAsync(moduleNamespace, name, provider, version);

    public Task<bool> UpdateModuleDescriptionAsync(string moduleNamespace, string name, string provider, string description) =>
        _modules.UpdateModuleDescriptionAsync(moduleNamespace, name, provider, description);

    public Task<ModuleExtractionDocument?> GetModuleExtractionAsync(
        string moduleNamespace,
        string name,
        string provider,
        string version) =>
        _moduleExtractions.GetModuleExtractionAsync(moduleNamespace, name, provider, version);

    public Task UpsertModuleExtractionAsync(
        string moduleNamespace,
        string name,
        string provider,
        string version,
        ModuleExtractionDocument document,
        string? sourceChecksum = null) =>
        _moduleExtractions.UpsertModuleExtractionAsync(moduleNamespace, name, provider, version, document, sourceChecksum);

    public Task<ModuleLlmContextDocument?> GetModuleLlmContextAsync(
        string moduleNamespace,
        string name,
        string provider,
        string version) =>
        _moduleExtractions.GetModuleLlmContextAsync(moduleNamespace, name, provider, version);

    public Task UpsertModuleLlmContextAsync(
        string moduleNamespace,
        string name,
        string provider,
        string version,
        ModuleLlmContextDocument document,
        string? sourceChecksum = null) =>
        _moduleExtractions.UpsertModuleLlmContextAsync(moduleNamespace, name, provider, version, document, sourceChecksum);

    public Task UpdateModuleMetadataAsync(
        string moduleNamespace,
        string name,
        string provider,
        string version,
        Action<ModuleArtifactMetadata> mutate) =>
        _moduleExtractions.UpdateModuleMetadataAsync(moduleNamespace, name, provider, version, mutate);

    public Task<IReadOnlyList<ModuleStorage>> ListModulesNeedingExtractionAsync(int limit) =>
        _moduleExtractions.ListModulesNeedingExtractionAsync(limit);

    public Task<ModuleExtractionAdminSummary> GetModuleExtractionAdminSummaryAsync() =>
        _moduleExtractions.GetModuleExtractionAdminSummaryAsync();

    public Task<ModuleExtractionAdminPage> ListModuleExtractionsAdminAsync(ModuleExtractionAdminQuery query) =>
        _moduleExtractions.ListModuleExtractionsAdminAsync(query);

    public Task<ModuleExtractionAdminDetail?> GetModuleExtractionAdminDetailAsync(
        string moduleNamespace,
        string name,
        string provider,
        string version) =>
        _moduleExtractions.GetModuleExtractionAdminDetailAsync(moduleNamespace, name, provider, version);

    public Task<IReadOnlyList<ModuleStorage>> ListModulesForExtractionBackfillAsync(int limit) =>
        _moduleExtractions.ListModulesForExtractionBackfillAsync(limit);

    public Task<IReadOnlyList<User>> GetUsersByEmailCaseInsensitiveAsync(string email) =>
        _users.GetUsersByEmailCaseInsensitiveAsync(email);

    public Task<User?> GetUserByEmailAsync(string email) =>
        _users.GetUserByEmailAsync(email);

    public Task<User?> GetUserByIdAsync(string id) =>
        _users.GetUserByIdAsync(id);

    public Task AddUserAsync(User user) =>
        _users.AddUserAsync(user);

    public Task UpdateUserAsync(User user) =>
        _users.UpdateUserAsync(user);

    public Task DeleteUserAsync(string userId) =>
        _users.DeleteUserAsync(userId);

    public Task<IEnumerable<User>> ListAllUsersAsync() =>
        _users.ListAllUsersAsync();

    public Task AddApiKeyAsync(ApiKey apiKey) =>
        _apiKeys.AddApiKeyAsync(apiKey);

    public Task<ApiKey?> GetApiKeyAsync(Guid id) =>
        _apiKeys.GetApiKeyAsync(id);

    public Task<IEnumerable<ApiKey>> GetApiKeysByUserAsync(string userId) =>
        _apiKeys.GetApiKeysByUserAsync(userId);

    public Task<IEnumerable<ApiKey>> GetSharedApiKeysAsync() =>
        _apiKeys.GetSharedApiKeysAsync();

    public Task<IEnumerable<ApiKey>> GetApiKeysByPrefixAsync(string prefix) =>
        _apiKeys.GetApiKeysByPrefixAsync(prefix);

    public Task UpdateApiKeyAsync(ApiKey apiKey) =>
        _apiKeys.UpdateApiKeyAsync(apiKey);

    public Task DeleteApiKeyAsync(ApiKey apiKey) =>
        _apiKeys.DeleteApiKeyAsync(apiKey);

    public Task RecordDownloadAsync(
        string moduleNamespace,
        string name,
        string provider,
        string version,
        string? clientIp,
        string? userAgent) =>
        _downloads.RecordDownloadAsync(moduleNamespace, name, provider, version, clientIp, userAgent);

    public async Task<bool> CheckConnectionAsync()
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public Task InitializeDatabase()
    {
        _dbUpMigrator.Migrate("postgres", _connectionString);
        return Task.CompletedTask;
    }
}
