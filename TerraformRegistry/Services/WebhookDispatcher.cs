using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TerraformRegistry.API.Interfaces;

namespace TerraformRegistry.Services;

public class WebhookDispatcher(IWebhookService webhookService, IHttpClientFactory httpClientFactory, ILogger<WebhookDispatcher> logger)
{
    public void FireEvent(string eventType, string @namespace, string name, string provider, string version)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var webhooks = await webhookService.GetActiveWebhooksForEventAsync(eventType);
                var payload = JsonSerializer.Serialize(new
                {
                    @event = eventType,
                    timestamp = DateTime.UtcNow.ToString("o"),
                    module = new { @namespace, name, provider, version }
                });

                var client = httpClientFactory.CreateClient("WebhookDelivery");
                client.Timeout = TimeSpan.FromSeconds(5);

                var deliveryTasks = webhooks.Select(async webhook =>
                {
                    try { await DeliverAsync(client, webhook.Url, webhook.Secret, payload); }
                    catch (Exception ex) { logger.LogError(ex, "Failed to deliver webhook {WebhookId} to {Url}", webhook.Id, webhook.Url); }
                });
                await Task.WhenAll(deliveryTasks);
            }
            catch (Exception ex) { logger.LogError(ex, "Failed to fire webhook event {EventType}", eventType); }
        });
    }

    private static async Task DeliverAsync(HttpClient client, string url, string? secret, string payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrEmpty(secret))
        {
            var signature = ComputeHmacSha256(payload, secret);
            request.Headers.Add("X-Signature-256", $"sha256={signature}");
        }
        await client.SendAsync(request);
    }

    private static string ComputeHmacSha256(string payload, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(keyBytes, payloadBytes);
        return Convert.ToHexStringLower(hash);
    }
}
