using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

public interface IVcsSourceService
{
    Task<IEnumerable<VcsSource>> ListVcsSourcesAsync(string userId);
    Task<VcsSource> CreateVcsSourceAsync(string userId, string @namespace, string name, string provider, string repoOwner, string repoName, string? patEncrypted, string webhookSecret);
    Task<VcsSource?> UpdateVcsSourceAsync(Guid id, string userId, string? repoOwner, string? repoName, string? patEncrypted, bool? isActive);
    Task<bool> DeleteVcsSourceAsync(Guid id, string userId);
    Task<VcsSource?> GetByRepoAsync(string repoOwner, string repoName);
}
