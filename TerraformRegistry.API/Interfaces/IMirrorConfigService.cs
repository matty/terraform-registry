using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

public interface IMirrorConfigService
{
    Task<MirrorConfigResponse> GetConfigAsync(CancellationToken cancellationToken);

    Task<MirrorConfigResponse> UpdateConfigAsync(
        MirrorConfigUpdateRequest request,
        string? updatedBy,
        CancellationToken cancellationToken);
}
