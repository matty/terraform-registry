using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

public interface IVcsConnectionService
{
    Task<IEnumerable<VcsConnection>> ListConnectionsAsync();
    Task<VcsConnection?> GetConnectionAsync(Guid id);
    Task<VcsConnection> CreateConnectionAsync(string? createdBy, string label, string provider, string? patEncrypted, string? defaultOrg, string webhookSecret);
    Task<VcsConnection?> UpdateConnectionAsync(Guid id, string? label, string? patEncrypted, string? defaultOrg, bool? isActive);
    Task<bool> DeleteConnectionAsync(Guid id);
    Task<IEnumerable<VcsConnection>> ListConnectionSummariesAsync();
}
