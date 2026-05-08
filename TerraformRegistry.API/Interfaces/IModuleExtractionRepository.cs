using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

/// <summary>
///     Stores generated module extraction documents and LLM context documents.
/// </summary>
public interface IModuleExtractionRepository
{
    Task<ModuleExtractionDocument?> GetModuleExtractionAsync(
        string @namespace,
        string name,
        string provider,
        string version);

    Task UpsertModuleExtractionAsync(
        string @namespace,
        string name,
        string provider,
        string version,
        ModuleExtractionDocument document,
        string? sourceChecksum = null);

    Task<ModuleLlmContextDocument?> GetModuleLlmContextAsync(
        string @namespace,
        string name,
        string provider,
        string version);

    Task UpsertModuleLlmContextAsync(
        string @namespace,
        string name,
        string provider,
        string version,
        ModuleLlmContextDocument document,
        string? sourceChecksum = null);

    Task UpdateModuleMetadataAsync(
        string @namespace,
        string name,
        string provider,
        string version,
        Action<ModuleArtifactMetadata> mutate);

    Task<IReadOnlyList<ModuleStorage>> ListModulesNeedingExtractionAsync(int limit);

    Task<ModuleExtractionAdminSummary> GetModuleExtractionAdminSummaryAsync();

    Task<ModuleExtractionAdminPage> ListModuleExtractionsAdminAsync(ModuleExtractionAdminQuery query);

    Task<ModuleExtractionAdminDetail?> GetModuleExtractionAdminDetailAsync(
        string @namespace,
        string name,
        string provider,
        string version);

    Task<IReadOnlyList<ModuleStorage>> ListModulesForExtractionBackfillAsync(int limit);
}
