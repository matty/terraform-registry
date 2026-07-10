namespace TerraformRegistry.API.Interfaces;

/// <summary>
///     Tracks completion of mandatory startup work that is not represented by a simple connection check.
/// </summary>
public interface IStartupReadiness
{
    bool IsStorageInitialized { get; }

    void MarkStorageInitialized();
}
