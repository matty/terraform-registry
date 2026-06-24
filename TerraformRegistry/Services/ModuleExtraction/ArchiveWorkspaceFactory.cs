using System.Formats.Tar;
using System.IO.Compression;
using Microsoft.Extensions.Options;

namespace TerraformRegistry.Services.ModuleExtraction;

public sealed class ArchiveWorkspaceFactory : IArchiveWorkspaceFactory
{
    private readonly ModuleExtractionOptions _options;

    public ArchiveWorkspaceFactory(IOptions<ModuleExtractionOptions> options) : this(options.Value)
    {
    }

    public ArchiveWorkspaceFactory(ModuleExtractionOptions options)
    {
        _options = options;
    }

    public async Task<ArchiveWorkspace> CreateAsync(Stream archiveStream, CancellationToken cancellationToken)
    {
        var workRoot = Path.Combine(_options.TempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workRoot);

        try
        {
            var archiveBytes = await ReadAllBytesAsync(archiveStream, _options.MaxArchiveBytes, cancellationToken);
            if (LooksLikeZip(archiveBytes))
            {
                using var zip = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read);
                zip.ExtractToDirectory(workRoot, overwriteFiles: true);
            }
            else
            {
                using var gzip = new GZipStream(new MemoryStream(archiveBytes), CompressionMode.Decompress);
                TarFile.ExtractToDirectory(gzip, workRoot, overwriteFiles: true);
            }

            return new ArchiveWorkspace(workRoot, NormalizeSingleTopLevelFolder(workRoot));
        }
        catch
        {
            if (Directory.Exists(workRoot))
                Directory.Delete(workRoot, recursive: true);

            throw;
        }
    }

    private static async Task<byte[]> ReadAllBytesAsync(
        Stream archiveStream,
        long maxArchiveBytes,
        CancellationToken cancellationToken)
    {
        if (maxArchiveBytes <= 0)
        {
            throw new InvalidOperationException("Module archive size limit must be greater than zero bytes.");
        }

        using var memory = new MemoryStream();
        var buffer = new byte[81920];

        while (true)
        {
            var read = await archiveStream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (memory.Length + read > maxArchiveBytes)
            {
                throw new InvalidOperationException(
                    $"Module archive exceeds the configured limit of {maxArchiveBytes} bytes.");
            }

            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return memory.ToArray();
    }

    private static bool LooksLikeZip(byte[] bytes)
    {
        return bytes.Length >= 4 &&
               bytes[0] == 0x50 &&
               bytes[1] == 0x4B &&
               (bytes[2] == 0x03 || bytes[2] == 0x05 || bytes[2] == 0x07) &&
               (bytes[3] == 0x04 || bytes[3] == 0x06 || bytes[3] == 0x08);
    }

    private static string NormalizeSingleTopLevelFolder(string workRoot)
    {
        var rootFiles = Directory.EnumerateFiles(workRoot).Take(1).ToList();
        if (rootFiles.Count > 0)
            return workRoot;

        var rootDirectories = Directory.EnumerateDirectories(workRoot).Take(2).ToList();
        return rootDirectories.Count == 1 ? rootDirectories[0] : workRoot;
    }
}
