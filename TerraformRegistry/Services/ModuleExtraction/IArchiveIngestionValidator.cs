namespace TerraformRegistry.Services.ModuleExtraction;

public interface IArchiveIngestionValidator
{
    Task<ValidatedArchive> PrepareAsync(Stream archiveStream, CancellationToken cancellationToken);
}
