namespace TerraformRegistry.Services;

public record WebhookEventData(
    string Id,
    string Event,
    string Action,
    string Timestamp,
    WebhookModuleData Module);

public record WebhookModuleData(
    string Namespace,
    string Name,
    string Provider,
    string Version,
    string? Description,
    string Source,
    string DownloadUrl);
