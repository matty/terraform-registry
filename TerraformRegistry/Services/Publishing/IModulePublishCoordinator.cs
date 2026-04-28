namespace TerraformRegistry.Services.Publishing;

public interface IModulePublishCoordinator
{
    Task<bool> PublishAsync(ModulePublishRequest request, CancellationToken cancellationToken);
}
