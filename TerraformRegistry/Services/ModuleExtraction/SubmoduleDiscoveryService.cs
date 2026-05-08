using TerraformRegistry.Models;

namespace TerraformRegistry.Services.ModuleExtraction;

public sealed class SubmoduleDiscoveryService
{
    public List<ModuleSubmodule> FindSubmodules(string rootPath)
    {
        var modulesRoot = Path.Combine(rootPath, "modules");
        if (!Directory.Exists(modulesRoot))
            return [];

        return Directory
            .EnumerateDirectories(modulesRoot)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new ModuleSubmodule
            {
                Path = ReadmeDiscoveryService.ToRelativePath(rootPath, path),
                Providers = []
            })
            .ToList();
    }
}
