using System.IO.Compression;
using System.Text.Json;
using TerraformRegistry.Models;

namespace TerraformRegistry.API.Utilities;

public static class ModulePackageMetadataExtractor
{
    public static async Task<ModuleMetadata?> ExtractAsync(Stream moduleContent)
    {
        if (!moduleContent.CanSeek)
            throw new ArgumentException("Module content stream must be seekable.", nameof(moduleContent));

        moduleContent.Position = 0;

        using var archive = new ZipArchive(moduleContent, ZipArchiveMode.Read, leaveOpen: true);
        var metadataEntry = archive.Entries.FirstOrDefault(e =>
            e.Name.Equals("module.json", StringComparison.OrdinalIgnoreCase) ||
            e.Name.Equals("metadata.json", StringComparison.OrdinalIgnoreCase));

        if (metadataEntry == null)
        {
            moduleContent.Position = 0;
            return null;
        }

        using var stream = metadataEntry.Open();
        var metadata = await JsonSerializer.DeserializeAsync<ModuleMetadata>(stream);
        moduleContent.Position = 0;
        return metadata;
    }

    public static ModuleMetadata Normalize(ModuleMetadata? metadata, string provider, string description)
    {
        var normalized = metadata ?? new ModuleMetadata();
        normalized.Description = !string.IsNullOrWhiteSpace(description) ? description : normalized.Description;
        normalized.Root ??= "main";
        normalized.Providers ??= new Dictionary<string, string> { [provider] = "*" };
        normalized.Submodules ??= [];
        normalized.Properties ??= new Dictionary<string, string>();
        return normalized;
    }
}
