using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

public interface IRuntimeSettingsService
{
    Task<RuntimeSetting?> GetAsync(string key, CancellationToken cancellationToken);
    Task SetAsync(string key, string valueJson, string? updatedBy, CancellationToken cancellationToken);
}
