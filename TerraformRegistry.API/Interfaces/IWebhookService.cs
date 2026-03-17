using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

public interface IWebhookService
{
    Task<IEnumerable<Webhook>> ListWebhooksAsync(string userId);
    Task<Webhook> CreateWebhookAsync(string userId, string url, string[] events, string? secret);
    Task<Webhook?> UpdateWebhookAsync(Guid webhookId, string userId, string? url, string[]? events, string? secret, bool? isActive);
    Task<bool> DeleteWebhookAsync(Guid webhookId, string userId);
    Task<IEnumerable<Webhook>> GetActiveWebhooksForEventAsync(string eventType);
}
