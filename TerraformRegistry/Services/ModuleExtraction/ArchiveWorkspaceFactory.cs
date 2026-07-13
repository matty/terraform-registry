using System.Formats.Tar;
using System.IO.Compression;
using Microsoft.Extensions.Options;

namespace TerraformRegistry.Services.ModuleExtraction;

public sealed class ArchiveWorkspaceFactory : IArchiveWorkspaceFactory
{
    private const int BufferSize = 1024 * 1024;
    private readonly ModuleExtractionOptions _options;

    public ArchiveWorkspaceFactory(IOptions<ModuleExtractionOptions> options) : this(options.Value)
    {
    }

    public ArchiveWorkspaceFactory(ModuleExtractionOptions options)
    {
        _options = options;
        _options.Validate();
    }

    public async Task<ArchiveWorkspace> CreateAsync(Stream archiveStream, CancellationToken cancellationToken)
    {
        var workRoot = Path.Join(_options.TempRoot, Guid.NewGuid().ToString("N"));
        var spoolPath = Path.Join(_options.TempRoot, $".{Guid.NewGuid():N}.archive");
        Directory.CreateDirectory(workRoot);

        try
        {
            await SpoolAsync(archiveStream, spoolPath, cancellationToken);
            if (await LooksLikeZipAsync(spoolPath, cancellationToken))
            {
                await ExtractZipAsync(spoolPath, workRoot, cancellationToken);
            }
            else
            {
                await ExtractTarGzAsync(spoolPath, workRoot, cancellationToken);
            }

            File.Delete(spoolPath);
            return new ArchiveWorkspace(workRoot, NormalizeSingleTopLevelFolder(workRoot));
        }
        catch
        {
            TryDeleteFile(spoolPath);
            if (Directory.Exists(workRoot))
            {
                Directory.Delete(workRoot, recursive: true);
            }

            throw;
        }
    }

    private async Task SpoolAsync(Stream source, string spoolPath, CancellationToken cancellationToken)
    {
        await using var destination = new FileStream(spoolPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[BufferSize];
        long copied = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            copied = checked(copied + read);
            if (copied > _options.MaxArchiveBytes)
            {
                throw new InvalidOperationException($"Module archive exceeds the configured limit of {_options.MaxArchiveBytes} bytes.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static async Task<bool> LooksLikeZipAsync(string spoolPath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(spoolPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var header = new byte[4];
        var read = await stream.ReadAsync(header, cancellationToken);
        return read == 4 && header[0] == 0x50 && header[1] == 0x4B &&
               (header[2] == 0x03 || header[2] == 0x05 || header[2] == 0x07) &&
               (header[3] == 0x04 || header[3] == 0x06 || header[3] == 0x08);
    }

    private async Task ExtractZipAsync(string spoolPath, string workRoot, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(spoolPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        long expandedTotal = 0;
        var entries = 0;

        foreach (var entry in archive.Entries)
        {
            ValidateEntry(entry.FullName, entry.Length, entry.CompressedLength, ref entries, expandedTotal);
            var destinationPath = GetSafeDestinationPath(workRoot, entry.FullName);
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using var input = entry.Open();
            await using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            expandedTotal = await CopyEntryAsync(input, output, expandedTotal, cancellationToken);
        }
    }

    private async Task ExtractTarGzAsync(string spoolPath, string workRoot, CancellationToken cancellationToken)
    {
        await using var archiveFile = new FileStream(spoolPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var gzip = new GZipStream(archiveFile, CompressionMode.Decompress);
        using var reader = new TarReader(gzip, leaveOpen: false);
        long expandedTotal = 0;
        var entries = 0;
        var compressedBytes = new FileInfo(spoolPath).Length;

        while (reader.GetNextEntry() is { } entry)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateEntry(entry.Name, entry.Length, compressedBytes, ref entries, expandedTotal);
            var destinationPath = GetSafeDestinationPath(workRoot, entry.Name);
            if (entry.EntryType is TarEntryType.Directory)
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            if (entry.EntryType is not TarEntryType.RegularFile and not TarEntryType.V7RegularFile)
            {
                throw new InvalidOperationException("Archive entries must be regular files or directories.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            expandedTotal = await CopyEntryAsync(
                entry.DataStream ?? Stream.Null,
                output,
                expandedTotal,
                cancellationToken);
        }
    }

    private void ValidateEntry(string entryName, long entryLength, long compressedLength, ref int entries, long expandedTotal)
    {
        if (string.IsNullOrWhiteSpace(entryName))
        {
            throw new InvalidOperationException("Archive entry path is required.");
        }

        entries = checked(entries + 1);
        if (entries > _options.MaxArchiveEntries)
        {
            throw new InvalidOperationException("Archive exceeds the configured entry count limit.");
        }

        if (entryLength > _options.MaxExpandedEntryBytes)
        {
            throw new InvalidOperationException("Archive entry exceeds the configured expanded entry limit.");
        }

        if (entryLength > 0 && (compressedLength <= 0 || entryLength / Math.Max(1, compressedLength) > _options.MaxCompressionRatio))
        {
            throw new InvalidOperationException("Archive entry exceeds the configured compression ratio limit.");
        }

        if (expandedTotal > _options.MaxExpandedArchiveBytes - entryLength)
        {
            throw new InvalidOperationException("Archive expanded content exceeds the configured limit.");
        }
    }

    private async Task<long> CopyEntryAsync(Stream input, Stream output, long expandedTotal, CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return expandedTotal;
            }

            if (expandedTotal > _options.MaxExpandedArchiveBytes - read)
            {
                throw new InvalidOperationException("Archive expanded content exceeds the configured limit.");
            }

            expandedTotal += read;
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static string GetSafeDestinationPath(string workRoot, string entryName)
    {
        if (Path.IsPathRooted(entryName))
        {
            throw new InvalidOperationException("Archive entry path must be relative.");
        }

        var root = Path.GetFullPath(workRoot);
        var destination = Path.GetFullPath(Path.Join(root, entryName));
        if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Archive entry path escapes the extraction root.");
        }

        return destination;
    }

    private static void TryDeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string NormalizeSingleTopLevelFolder(string workRoot)
    {
        var rootFiles = Directory.EnumerateFiles(workRoot).Take(1).ToList();
        if (rootFiles.Count > 0)
        {
            return workRoot;
        }

        var rootDirectories = Directory.EnumerateDirectories(workRoot).Take(2).ToList();
        return rootDirectories.Count == 1 ? rootDirectories[0] : workRoot;
    }
}
