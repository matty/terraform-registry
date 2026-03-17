using System.Security.Cryptography;
using System.Text;
using TerraformRegistry.API.Interfaces;

namespace TerraformRegistry.Services;

public class WebhookDispatcher(
    IWebhookService webhookService,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<WebhookDispatcher> logger)
{
    public void FireEvent(string eventType, string @namespace, string name, string provider, string version, string? description)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var webhooks = await webhookService.GetActiveWebhooksForEventAsync(eventType);

                var baseUrl = configuration["BaseUrl"]?.TrimEnd('/') ?? string.Empty;
                var action = eventType.Contains('.') ? eventType[(eventType.LastIndexOf('.') + 1)..] : eventType;

                var eventData = new WebhookEventData(
                    Id: "wh_" + Guid.NewGuid().ToString("N"),
                    Event: eventType,
                    Action: action,
                    Timestamp: DateTime.UtcNow.ToString("o"),
                    Module: new WebhookModuleData(
                        Namespace: @namespace,
                        Name: name,
                        Provider: provider,
                        Version: version,
                        Description: description,
                        Source: $"{baseUrl}/{@namespace}/{name}/{provider}",
                        DownloadUrl: $"/v1/modules/{@namespace}/{name}/{provider}/{version}/download"));

                var client = httpClientFactory.CreateClient("WebhookDelivery");

                var deliveryTasks = webhooks.Select(async webhook =>
                {
                    try
                    {
                        var formatter = GetFormatter(webhook.Format);
                        var payload = formatter.FormatPayload(eventData, webhook.Template);
                        await DeliverAsync(client, webhook.Url, webhook.Secret, payload, eventData.Id, eventType);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to deliver webhook {WebhookId} to {Url}", webhook.Id, webhook.Url);
                    }
                });
                await Task.WhenAll(deliveryTasks);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to fire webhook event {EventType}", eventType);
            }
        });
    }

    private static IWebhookFormatter GetFormatter(string format) => format switch
    {
        "discord" => new DiscordFormatter(),
        "slack" => new SlackFormatter(),
        "teams" => new TeamsFormatter(),
        "custom" => new CustomFormatter(),
        _ => new GenericFormatter()
    };

    private static async Task DeliverAsync(HttpClient client, string url, string? secret, string payload, string webhookId, string eventType)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        request.Headers.Add("X-Webhook-Id", webhookId);
        request.Headers.Add("X-Webhook-Event", eventType);

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
