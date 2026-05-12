using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Migrations;
using TerraformRegistry.Models;
using TerraformRegistry.Services.Sqlite;

namespace TerraformRegistry.Services;

/// <summary>
///     SQLite compatibility facade for local development/storage.
/// </summary>
public class SqliteDatabaseService : IDatabaseService, IInitializableDb
{
    private readonly string _connectionString;
    private readonly DbUpMigrator _dbUpMigrator;
    private readonly SqliteApiKeyRepository _apiKeys;
    private readonly SqliteModuleDownloadRecorder _downloads;
    private readonly SqliteModuleExtractionRepository _moduleExtractions;
    private readonly SqliteModuleRepository _modules;
    private readonly SqliteUserRepository _users;

    public SqliteDatabaseService(string connectionString, string baseUrl, ILogger<SqliteDatabaseService> logger,
        DbUpMigrator dbUpMigrator)
    {
        _connectionString = connectionString;
        _dbUpMigrator = dbUpMigrator;
        _modules = new SqliteModuleRepository(connectionString, baseUrl, logger);
        _moduleExtractions = new SqliteModuleExtractionRepository(connectionString);
        _users = new SqliteUserRepository(connectionString);
        _apiKeys = new SqliteApiKeyRepository(connectionString);
        _downloads = new SqliteModuleDownloadRecorder(connectionString);
    }

    public Task InitializeDatabase()
    {
        _dbUpMigrator.Migrate("sqlite", _connectionString);
        return Task.CompletedTask;
    }

    public Task<ModuleList> ListModulesAsync(ModuleSearchRequest request) =>
        _modules.ListModulesAsync(request);

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
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
