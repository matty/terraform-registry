using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

public interface IWebhookService
{
    Task<IEnumerable<Webhook>> ListWebhooksAsync(string userId);
    Task<IEnumerable<Webhook>> ListAllWebhooksAsync();
    Task<Webhook> CreateWebhookAsync(string userId, string url, string[] events, string? secret, string format = "generic", string? template = null);
    Task<Webhook?> UpdateWebhookAsync(Guid webhookId, string userId, string? url, string[]? events, string? secret, bool? isActive, string? format, string? template);
    Task<Webhook?> GetWebhookAsync(Guid webhookId, string userId);
    Task<bool> DeleteWebhookAsync(Guid webhookId, string userId);
    Task<IEnumerable<Webhook>> GetActiveWebhooksForEventAsync(string eventType);
}
