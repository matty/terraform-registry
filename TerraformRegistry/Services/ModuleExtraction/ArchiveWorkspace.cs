namespace TerraformRegistry.Services.ModuleExtraction;

public sealed class ArchiveWorkspace : IAsyncDisposable
{
    public ArchiveWorkspace(string workRoot, string rootPath)
    {
        WorkRoot = Path.GetFullPath(workRoot);
        RootPath = Path.GetFullPath(rootPath);
    }

    public string WorkRoot { get; }

    public string RootPath { get; }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (Directory.Exists(WorkRoot))
                Directory.Delete(WorkRoot, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return ValueTask.CompletedTask;
    }
}
