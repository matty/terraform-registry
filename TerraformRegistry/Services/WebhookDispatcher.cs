using System.Security.Cryptography;
using System.Text;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services;

public class WebhookDispatcher(
    IWebhookService webhookService,
    IOutboxEventRepository outboxRepository,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    WebhookUrlValidator webhookUrlValidator,
    ILogger<WebhookDispatcher> logger)
{
    public Task FireEventAsync(string eventType, string @namespace, string name, string provider, string version, string? description,
        CancellationToken cancellationToken = default)
    {
        var payload = new WebhookOutboxPayload("wh_" + Guid.NewGuid().ToString("N"), eventType, @namespace, name, provider, version, description);
        var now = DateTime.UtcNow;
        return outboxRepository.EnqueueAsync(new OutboxEvent
        {
            Id = Guid.NewGuid(),
            Kind = WebhookOutboxDeliveryHandler.Kind,
            IdempotencyKey = $"webhook:{payload.Id}",
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload),
            State = OutboxEventState.Pending,
            CreatedAt = now,
            UpdatedAt = now
        }, cancellationToken);
    }

    public async Task DeliverAsync(WebhookOutboxPayload payload, CancellationToken cancellationToken)
    {
        var webhooks = await webhookService.GetActiveWebhooksForEventAsync(payload.EventType);

        var baseUrl = configuration["BaseUrl"]?.TrimEnd('/') ?? string.Empty;
        var action = payload.EventType.Contains('.', StringComparison.Ordinal) ? payload.EventType[(payload.EventType.LastIndexOf('.') + 1)..] : payload.EventType;

        var eventData = new WebhookEventData(payload.Id, payload.EventType, action, DateTime.UtcNow.ToString("o"),
            new WebhookModuleData(payload.Namespace, payload.Name, payload.Provider, payload.Version, payload.Description,
                $"{baseUrl}/{payload.Namespace}/{payload.Name}/{payload.Provider}",
                $"/v1/modules/{payload.Namespace}/{payload.Name}/{payload.Provider}/{payload.Version}/download"));

        var client = httpClientFactory.CreateClient("WebhookDelivery");
        foreach (var webhook in webhooks)
        {
            var formatter = GetFormatter(webhook.Format);
            var body = formatter.FormatPayload(eventData, webhook.Template);
            await DeliverAsync(client, webhook.Url, webhook.Secret, body, eventData.Id, payload.EventType, cancellationToken);
        }
    }

    public async Task<(bool Success, string? Error)> SendTestAsync(Webhook webhook)
    {
        var baseUrl = configuration["BaseUrl"]?.TrimEnd('/') ?? string.Empty;

        var eventData = new WebhookEventData(
            Id: "wh_test_" + Guid.NewGuid().ToString("N"),
            Event: "module.published",
            Action: "published",
            Timestamp: DateTime.UtcNow.ToString("o"),
            Module: new WebhookModuleData(
                Namespace: "example",
                Name: "test-module",
                Provider: "aws",
                Version: "1.0.0",
                Description: "This is a test webhook delivery",
                Source: $"{baseUrl}/example/test-module/aws",
                DownloadUrl: "/v1/modules/example/test-module/aws/1.0.0/download"));

        try
        {
            var formatter = GetFormatter(webhook.Format);
            var payload = formatter.FormatPayload(eventData, webhook.Template);
            var client = httpClientFactory.CreateClient("WebhookDelivery");
            await DeliverAsync(client, webhook.Url, webhook.Secret, payload, eventData.Id, "module.published");
            return (true, null);
        }
        catch (Exception ex)
        {
            RegistryLog.Error(logger, ex, "Test webhook delivery failed for {WebhookId} to {Url}", webhook.Id, webhook.Url);
            return (false, ex.Message);
        }
    }

    private static readonly Dictionary<string, IWebhookFormatter> Formatters = new(StringComparer.Ordinal)
    {
        ["generic"] = new GenericFormatter(),
        ["discord"] = new DiscordFormatter(),
        ["slack"] = new SlackFormatter(),
        ["teams"] = new TeamsFormatter(),
        ["custom"] = new CustomFormatter(),
    };

    private static IWebhookFormatter GetFormatter(string format) =>
        Formatters.GetValueOrDefault(format, Formatters["generic"]);

    private async Task DeliverAsync(HttpClient client, string url, string? secret, string payload, string webhookId, string eventType, CancellationToken cancellationToken = default)
    {
        var validatedEndpoint = await webhookUrlValidator.ValidateOutboundWebhookUrlAsync(url, cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, validatedEndpoint.Uri)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        WebhookPinnedConnectionHelper.AttachValidatedAddresses(request, validatedEndpoint.Addresses);

        request.Headers.Add("X-Webhook-Id", webhookId);
        request.Headers.Add("X-Webhook-Event", eventType);

        if (!string.IsNullOrEmpty(secret))
        {
            var signature = ComputeHmacSha256(payload, secret);
            request.Headers.Add("X-Signature-256", $"sha256={signature}");
        }

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static string ComputeHmacSha256(string payload, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(keyBytes, payloadBytes);
        return Convert.ToHexStringLower(hash);
    }
}

public sealed record WebhookOutboxPayload(string Id, string EventType, string Namespace, string Name, string Provider, string Version, string? Description);
