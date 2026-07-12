namespace TerraformRegistry.Services;

public interface INamespaceMaintainerStore
{
    Task<string?> GetMaintainerAsync(string namespaceName);
}

public sealed class NamespaceAuthorizationService(INamespaceMaintainerStore store)
{
    public async Task<bool> CanMutateAsync(string @namespace, string userId, bool isSystemOverride)
    {
        if (isSystemOverride) return true;

        var maintainer = await store.GetMaintainerAsync(@namespace);
        return string.Equals(maintainer, userId, StringComparison.Ordinal);
    }
}
