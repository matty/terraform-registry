namespace TerraformRegistry.Services.ModuleExtraction;

public sealed class ArchiveIngestionValidator(
    IArchiveWorkspaceFactory workspaceFactory,
    ModuleExtractionOptions options) : IArchiveIngestionValidator
{
    private const int BufferSize = 1024 * 1024;

    public async Task<ValidatedArchive> PrepareAsync(Stream archiveStream, CancellationToken cancellationToken)
    {
        options.Validate();
        Directory.CreateDirectory(options.TempRoot);
        var path = Path.Combine(options.TempRoot, $".{Guid.NewGuid():N}.upload");

        try
        {
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[BufferSize];
                long copied = 0;
                while (true)
                {
                    var read = await archiveStream.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    copied = checked(copied + read);
                    if (copied > options.MaxArchiveBytes)
                    {
                        throw new InvalidOperationException($"Module archive exceeds the configured limit of {options.MaxArchiveBytes} bytes.");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            await using (var validationInput = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                             BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var workspace = await workspaceFactory.CreateAsync(validationInput, cancellationToken))
            {
            }

            return new ValidatedArchive(path);
        }
        catch
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            throw;
        }
    }
}
