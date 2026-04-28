using System.Formats.Tar;
using System.IO.Compression;

namespace TerraformRegistry.Tests.Support;

public static class TestArchiveBuilder
{
    public static byte[] CreateZipBytes(params (string Path, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = zip.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    public static async Task CreateTarGzAsync(string path, params (string Path, string Content)[] entries)
    {
        await using var file = File.Create(path);
        await using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        await using var writer = new TarWriter(gzip, leaveOpen: false);

        foreach (var (entryPath, content) in entries)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            var entry = new PaxTarEntry(TarEntryType.RegularFile, entryPath)
            {
                DataStream = new MemoryStream(bytes)
            };

            await writer.WriteEntryAsync(entry);
        }
    }
}
