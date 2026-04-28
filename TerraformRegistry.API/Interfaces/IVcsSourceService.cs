using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

public interface IVcsSourceService
{
    Task<IEnumerable<VcsSource>> ListVcsSourcesAsync(string userId);
    Task<VcsSource> CreateVcsSourceAsync(string userId, string @namespace, string name, string provider, string repoOwner, string repoName, Guid connectionId);
    Task<VcsSource?> UpdateVcsSourceAsync(Guid id, string userId, string? repoOwner, string? repoName, Guid? connectionId, bool? isActive);
    Task<bool> DeleteVcsSourceAsync(Guid id, string userId);
    Task<VcsSource?> GetAsync(Guid id);
    Task<VcsSource?> GetByRepoAsync(string repoOwner, string repoName);
    Task<VcsSource?> GetByModuleAsync(string @namespace, string name, string provider);
    Task<VcsSource?> UpdateSyncStateAsync(Guid id, string status, string? lastPublishedVersion, string? error);
}
