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
public class PostgreSqlDatabaseService : IDatabaseService, IInitializableDb
{
    private readonly string _connectionString;
    private readonly DbUpMigrator _dbUpMigrator;
    private readonly IApiKeyRepository _apiKeys;
    private readonly IModuleDownloadRecorder _downloads;
    private readonly IModuleExtractionRepository _moduleExtractions;
    private readonly IModuleRepository _modules;
    private readonly IUserRepository _users;

    public PostgreSqlDatabaseService(string connectionString, string baseUrl, ILogger<PostgreSqlDatabaseService> logger,
        DbUpMigrator dbUpMigrator)
    {
        _connectionString = connectionString;
        _dbUpMigrator = dbUpMigrator;
        _modules = new PostgreSqlModuleRepository(connectionString, baseUrl, logger);
        _moduleExtractions = new PostgreSqlModuleExtractionRepository(connectionString);
        _users = new PostgreSqlUserRepository(connectionString);
        _apiKeys = new PostgreSqlApiKeyRepository(connectionString);
        _downloads = new PostgreSqlModuleDownloadRecorder(connectionString, logger);
    }

    public Task<ModuleList> ListModulesAsync(ModuleSearchRequest request) =>
        _modules.ListModulesAsync(request);

    public Task<Module?> GetModuleAsync(string @namespace, string name, string provider, string version) =>
        _modules.GetModuleAsync(@namespace, name, provider, version);

    public Task<ModuleVersions> GetModuleVersionsAsync(string @namespace, string name, string provider) =>
        _modules.GetModuleVersionsAsync(@namespace, name, provider);

    public Task<ModuleStorage?> GetModuleStorageAsync(string @namespace, string name, string provider, string version) =>
        _modules.GetModuleStorageAsync(@namespace, name, provider, version);

    public Task<bool> AddModuleAsync(ModuleStorage module) =>
        _modules.AddModuleAsync(module);

    public Task<bool> RemoveModuleAsync(ModuleStorage module) =>
        _modules.RemoveModuleAsync(module);

    public Task<bool> RemoveModuleExactAsync(ModuleStorage module) =>
        _modules.RemoveModuleExactAsync(module);

    public Task<bool> RemoveDeletedModuleAsync(string @namespace, string name, string provider, string version) =>
        _modules.RemoveDeletedModuleAsync(@namespace, name, provider, version);

    public Task<bool> AddDeletedModuleAsync(ModuleStorage module) =>
        _modules.AddDeletedModuleAsync(module);

    public Task<bool> ReplaceModuleExactAsync(ModuleStorage existingModule, ModuleStorage newModule) =>
        _modules.ReplaceModuleExactAsync(existingModule, newModule);

    public Task<bool> SoftDeleteModuleAsync(string @namespace, string name, string provider, string version) =>
        _modules.SoftDeleteModuleAsync(@namespace, name, provider, version);

    public Task<bool> RestoreModuleAsync(string @namespace, string name, string provider, string version) =>
        _modules.RestoreModuleAsync(@namespace, name, provider, version);

    public Task<ModuleList> ListDeletedModulesAsync(ModuleSearchRequest request) =>
        _modules.ListDeletedModulesAsync(request);

    public Task<ModuleStorage?> GetModuleStorageIncludingDeletedAsync(
        string @namespace,
        string name,
        string provider,
        string version) =>
        _modules.GetModuleStorageIncludingDeletedAsync(@namespace, name, provider, version);

    public Task<bool> UpdateModuleDescriptionAsync(string @namespace, string name, string provider, string description) =>
        _modules.UpdateModuleDescriptionAsync(@namespace, name, provider, description);

    public Task<ModuleExtractionDocument?> GetModuleExtractionAsync(
        string @namespace,
        string name,
        string provider,
        string version) =>
        _moduleExtractions.GetModuleExtractionAsync(@namespace, name, provider, version);

    public Task UpsertModuleExtractionAsync(
        string @namespace,
        string name,
        string provider,
        string version,
        ModuleExtractionDocument document,
        string? sourceChecksum = null) =>
        _moduleExtractions.UpsertModuleExtractionAsync(@namespace, name, provider, version, document, sourceChecksum);

    public Task<ModuleLlmContextDocument?> GetModuleLlmContextAsync(
        string @namespace,
        string name,
        string provider,
        string version) =>
        _moduleExtractions.GetModuleLlmContextAsync(@namespace, name, provider, version);

    public Task UpsertModuleLlmContextAsync(
        string @namespace,
        string name,
        string provider,
        string version,
        ModuleLlmContextDocument document,
        string? sourceChecksum = null) =>
        _moduleExtractions.UpsertModuleLlmContextAsync(@namespace, name, provider, version, document, sourceChecksum);

    public Task UpdateModuleMetadataAsync(
        string @namespace,
        string name,
        string provider,
        string version,
        Action<ModuleArtifactMetadata> mutate) =>
        _moduleExtractions.UpdateModuleMetadataAsync(@namespace, name, provider, version, mutate);

    public Task<IReadOnlyList<ModuleStorage>> ListModulesNeedingExtractionAsync(int limit) =>
        _moduleExtractions.ListModulesNeedingExtractionAsync(limit);

    public Task<ModuleExtractionAdminSummary> GetModuleExtractionAdminSummaryAsync() =>
        _moduleExtractions.GetModuleExtractionAdminSummaryAsync();

    public Task<ModuleExtractionAdminPage> ListModuleExtractionsAdminAsync(ModuleExtractionAdminQuery query) =>
        _moduleExtractions.ListModuleExtractionsAdminAsync(query);

    public Task<ModuleExtractionAdminDetail?> GetModuleExtractionAdminDetailAsync(
        string @namespace,
        string name,
        string provider,
        string version) =>
        _moduleExtractions.GetModuleExtractionAdminDetailAsync(@namespace, name, provider, version);

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
        string @namespace,
        string name,
        string provider,
        string version,
        string? clientIp,
        string? userAgent) =>
        _downloads.RecordDownloadAsync(@namespace, name, provider, version, clientIp, userAgent);

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
