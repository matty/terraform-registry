using System.IO.Compression;

namespace TerraformRegistry.API.Utilities;

public static class ModuleArchiveValidator
{
    public static async Task<MemoryStream> ValidateAndNormalizeZipAsync(string packageFileName, Stream moduleContent)
    {
        if (string.IsNullOrWhiteSpace(packageFileName) ||
            !packageFileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Module package must be uploaded as a .zip archive.", nameof(packageFileName));

        var normalized = new MemoryStream();
        await moduleContent.CopyToAsync(normalized);

        if (normalized.Length == 0)
            throw new ArgumentException("Module package is empty.", nameof(moduleContent));

        normalized.Position = 0;

        try
        {
            using var archive = new ZipArchive(normalized, ZipArchiveMode.Read, leaveOpen: true);
            var hasTerraformFile = false;

            foreach (var entry in archive.Entries)
            {
                ValidateEntryPath(entry.FullName);

                if (!string.IsNullOrEmpty(entry.Name) &&
                    entry.Name.EndsWith(".tf", StringComparison.OrdinalIgnoreCase))
                    hasTerraformFile = true;
            }

            if (!hasTerraformFile)
                throw new ArgumentException("Module package must contain at least one .tf file.", nameof(moduleContent));
        }
        catch (InvalidDataException ex)
        {
            throw new ArgumentException("Module package must be a valid .zip archive.", nameof(moduleContent), ex);
        }

        normalized.Position = 0;
        return normalized;
    }

    private static void ValidateEntryPath(string entryPath)
    {
        if (string.IsNullOrWhiteSpace(entryPath))
            return;

        var normalizedPath = entryPath.Replace('\\', '/');
        if (normalizedPath.StartsWith("/", StringComparison.Ordinal))
            throw new ArgumentException("Module package contains an invalid absolute path.");

        var segments = normalizedPath.Split(['/'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment == ".."))
            throw new ArgumentException("Module package contains a path traversal entry.");
    }
}
