namespace TerraformRegistry.Services.ModuleExtraction;

public sealed class ValidatedArchive(string path) : IAsyncDisposable
{
    public Stream OpenRead() => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    public ValueTask DisposeAsync()
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return ValueTask.CompletedTask;
    }
}
