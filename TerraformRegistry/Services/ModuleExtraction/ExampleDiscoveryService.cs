using TerraformRegistry.Models;

namespace TerraformRegistry.Services.ModuleExtraction;

public sealed class ExampleDiscoveryService
{
    public List<ModuleExampleDefinition> FindExamples(string rootPath)
    {
        var examplesRoot = Path.Combine(rootPath, "examples");
        if (!Directory.Exists(examplesRoot))
            return [];

        return Directory
            .EnumerateDirectories(examplesRoot)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new ModuleExampleDefinition
            {
                Name = Path.GetFileName(path),
                Path = ReadmeDiscoveryService.ToRelativePath(rootPath, path),
                ReadmePath = FindReadme(rootPath, path)
            })
            .ToList();
    }

    private static string? FindReadme(string rootPath, string examplePath)
    {
        var readmePath = Directory
            .EnumerateFiles(examplePath, "README*", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path).Equals("README.md", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return readmePath == null ? null : ReadmeDiscoveryService.ToRelativePath(rootPath, readmePath);
    }
}
