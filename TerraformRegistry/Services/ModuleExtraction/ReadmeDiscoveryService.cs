using TerraformRegistry.Models;

namespace TerraformRegistry.Services.ModuleExtraction;

public sealed class ReadmeDiscoveryService
{
    public ModuleReadmeDocument? FindPrimary(string rootPath)
    {
        if (!Directory.Exists(rootPath))
            return null;

        var readmePath = Directory
            .EnumerateFiles(rootPath, "README*", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path).Equals("README.md", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (readmePath == null)
            return null;

        var markdown = File.ReadAllText(readmePath);
        return new ModuleReadmeDocument
        {
            Path = ToRelativePath(rootPath, readmePath),
            Title = FindTitle(markdown),
            Markdown = markdown
        };
    }

    private static string? FindTitle(string markdown)
    {
        foreach (var line in markdown.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
                return trimmed[2..].Trim();
        }

        return null;
    }

    internal static string ToRelativePath(string rootPath, string path)
    {
        return Path.GetRelativePath(rootPath, path).Replace(Path.DirectorySeparatorChar, '/');
    }
}
