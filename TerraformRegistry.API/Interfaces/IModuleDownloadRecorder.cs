namespace TerraformRegistry.API.Interfaces;

/// <summary>
///     Records module download events for analytics.
/// </summary>
public interface IModuleDownloadRecorder
{
    Task RecordDownloadAsync(
        string @namespace,
        string name,
        string provider,
        string version,
        string? clientIp,
        string? userAgent);
}
