using System.Collections.Concurrent;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services;

/// <summary>
///     Implementation of a database service with in-memory storage
/// </summary>
public class InMemoryDatabaseService(string baseUrl) : IDatabaseService
{
    private readonly ConcurrentDictionary<string, ModuleStorage> _modules = new();

    /// <summary>
    ///     Lists all modules based on search criteria
    /// </summary>
    public Task<ModuleList> ListModulesAsync(ModuleSearchRequest request)
    {
        var filteredModules = _modules.Values.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(request.Q))
            filteredModules = filteredModules.Where(m =>
                m.Name.Contains(request.Q, StringComparison.OrdinalIgnoreCase) ||
                m.Description.Contains(request.Q, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(request.Namespace))
            filteredModules = filteredModules.Where(m => m.Namespace == request.Namespace);

        if (!string.IsNullOrWhiteSpace(request.Provider))
            filteredModules = filteredModules.Where(m => m.Provider == request.Provider);

        // Group by namespace, name, provider to get unique modules
        var modules = filteredModules
            .GroupBy(m => new { m.Namespace, m.Name, m.Provider })
            .Select(g => g.OrderByDescending(m => m.Version).First()) // Take latest version
            .Skip(request.Offset)
            .Take(request.Limit)
            .ToList();

        var result = new ModuleList
        {
            Modules = modules.Select(m => new ModuleListItem
            {
                Id = $"{m.Namespace}/{m.Name}/{m.Provider}",
                Owner = m.Namespace,
                Namespace = m.Namespace,
                Name = m.Name,
                Version = m.Version,
                Provider = m.Provider,
                Description = m.Description,
                PublishedAt = m.PublishedAt.ToString("o"),
                Versions = _modules.Values
                    .Where(v => v.Namespace == m.Namespace && v.Name == m.Name && v.Provider == m.Provider)
                    .Select(v => v.Version)
                    .OrderByDescending(v => v)
                    .ToList(),
                DownloadUrl = $"{baseUrl}/v1/modules/{m.Namespace}/{m.Name}/{m.Provider}/{m.Version}/download"
            }).ToList(),
            Meta = new Dictionary<string, string>
            {
                { "limit", request.Limit.ToString() },
                { "current_offset", request.Offset.ToString() }
            }
        };

        return Task.FromResult(result);
    }

    /// <summary>
    ///     Gets detailed information about a specific module
    /// </summary>
    public Task<Module?> GetModuleAsync(string @namespace, string name, string provider, string version)
    {
        var key = GetModuleKey(@namespace, name, provider, version);
        if (!_modules.TryGetValue(key, out var storage)) return Task.FromResult<Module?>(null);

        var versions = _modules.Values
            .Where(m => m.Namespace == @namespace && m.Name == name && m.Provider == provider)
            .Select(m => m.Version)
            .OrderByDescending(v => v)
            .ToList();

        var module = new Module
        {
            Id = $"{@namespace}/{name}/{provider}/{version}",
            Owner = @namespace,
            Namespace = @namespace,
            Name = name,
            Version = version,
            Provider = provider,
            Description = storage.Description,
            PublishedAt = storage.PublishedAt.ToString("o"),
            Versions = versions,
            Root = "root",
            Submodules = new List<ModuleSubmodule>(), // Assuming no submodules for simplicity
            Providers = new Dictionary<string, string>
            {
                { provider, ">=0.12" } // Simplified provider constraints
            },
            DownloadUrl = $"{baseUrl}/v1/modules/{@namespace}/{name}/{provider}/{version}/download"
        };

        return Task.FromResult<Module?>(module);
    }

    /// <summary>
    ///     Gets all versions of a specific module
    /// </summary>
    public Task<ModuleVersions> GetModuleVersionsAsync(string @namespace, string name, string provider)
    {
        var versions = _modules.Values
            .Where(m => m.Namespace == @namespace && m.Name == name && m.Provider == provider)
            .Select(m => m.Version)
            .OrderByDescending(v => v)
            .ToList();

        return Task.FromResult(new ModuleVersions { Versions = versions });
    }

    /// <summary>
    ///     Gets the storage path information for a specific module version
    /// </summary>
    public Task<ModuleStorage?> GetModuleStorageAsync(string @namespace, string name, string provider, string version)
    {
        var key = GetModuleKey(@namespace, name, provider, version);
        if (!_modules.TryGetValue(key, out var storage)) return Task.FromResult<ModuleStorage?>(null);

        return Task.FromResult<ModuleStorage?>(storage);
    }

    /// <summary>
    ///     Adds a new module to the database
    /// </summary>
    public Task<bool> AddModuleAsync(ModuleStorage module)
    {
        var key = GetModuleKey(module.Namespace, module.Name, module.Provider, module.Version);

        // Check if module already exists
        if (_modules.ContainsKey(key)) return Task.FromResult(false);

        return Task.FromResult(_modules.TryAdd(key, module));
    }

    /// <summary>
    ///     Removes a module from the in-memory database
    /// </summary>
    public Task<bool> RemoveModuleAsync(ModuleStorage module)
    {
        var key = GetModuleKey(module.Namespace, module.Name, module.Provider, module.Version);
        return Task.FromResult(_modules.TryRemove(key, out _));
    }

    /// <summary>
    ///     Generates a unique key for a module version
    /// </summary>
    private static string GetModuleKey(string @namespace, string name, string provider, string version)
    {
        return $"{@namespace}/{name}/{provider}/{version}".ToLowerInvariant();
    }
}