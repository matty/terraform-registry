namespace TerraformRegistry.Services.ModuleExtraction;

public interface IArchiveWorkspaceFactory
{
    Task<ArchiveWorkspace> CreateAsync(Stream archiveStream, CancellationToken cancellationToken);
}
