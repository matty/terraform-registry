namespace TerraformRegistry.Models;

/// <summary>
/// Resolves the go-getter archive hint for a stored module artifact.
/// </summary>
public static class ModuleArchiveFormat
{
    public static string GetFileSuffix(ModuleArtifactMetadata? metadata)
    {
        var recordedFormat = metadata?.Source?.ArchiveFormat;
        return string.Equals(recordedFormat, "tar.gz", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(recordedFormat, "tgz", StringComparison.OrdinalIgnoreCase)
            ? ".tar.gz"
            : ".zip";
    }

    public static string? GetGoGetterHint(ModuleStorage moduleStorage)
    {
        var recordedFormat = moduleStorage.Metadata.Source?.ArchiveFormat;
        if (string.Equals(recordedFormat, "tar.gz", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(recordedFormat, "tgz", StringComparison.OrdinalIgnoreCase))
        {
            return "tar.gz";
        }

        return moduleStorage.FilePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
               moduleStorage.FilePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase)
            ? "tar.gz"
            : null;
    }
}
