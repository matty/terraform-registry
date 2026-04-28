using System.Text.Json;
using System.Text.Json.Serialization;

namespace TerraformRegistry.Services;

public interface IWebhookFormatter
{
    string FormatPayload(WebhookEventData eventData, string? template);
}

public class GenericFormatter : IWebhookFormatter
{
    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string FormatPayload(WebhookEventData eventData, string? template)
    {
        return JsonSerializer.Serialize(eventData, CamelCaseOptions);
    }
}

public class DiscordFormatter : IWebhookFormatter
{
    private static readonly Dictionary<string, int> EventColors = new()
    {
        ["module.published"] = 3066993,
        ["module.deleted"] = 15158332,
        ["module.restored"] = 3447003,
        ["module.purged"] = 15105570,
    };

    public string FormatPayload(WebhookEventData eventData, string? template)
    {
        var color = EventColors.GetValueOrDefault(eventData.Event, 0);
        var title = $"Module {eventData.Action}: {eventData.Module.Namespace}/{eventData.Module.Name}";
        var description = $"{eventData.Module.Provider} v{eventData.Module.Version}";

        if (!string.IsNullOrWhiteSpace(template))
        {
            var (customTitle, customBody) = TemplateOverrideParser.ParseTemplateOverrides(template, eventData);
            if (customTitle != null) title = customTitle;
            if (customBody != null) description = customBody;
        }

        var payload = new
        {
            embeds = new[]
            {
                new
                {
                    title,
                    description,
                    color,
                    fields = new[]
                    {
                        new { name = "Module", value = $"{eventData.Module.Namespace}/{eventData.Module.Name}/{eventData.Module.Provider}", inline = true },
                        new { name = "Version", value = eventData.Module.Version, inline = true },
                    },
                    timestamp = eventData.Timestamp,
                },
            },
        };

        return JsonSerializer.Serialize(payload);
    }
}

public class SlackFormatter : IWebhookFormatter
{
    public string FormatPayload(WebhookEventData eventData, string? template)
    {
        var title = $"Module {eventData.Action}: {eventData.Module.Namespace}/{eventData.Module.Name}";
        var body = $"*{eventData.Module.Provider}* v{eventData.Module.Version}";

        if (!string.IsNullOrWhiteSpace(template))
        {
            var (customTitle, customBody) = TemplateOverrideParser.ParseTemplateOverrides(template, eventData);
            if (customTitle != null) title = customTitle;
            if (customBody != null) body = customBody;
        }

        var payload = new
        {
            blocks = new object[]
            {
                new
                {
                    type = "header",
                    text = new { type = "plain_text", text = title },
                },
                new
                {
                    type = "section",
                    text = new { type = "mrkdwn", text = body },
                },
            },
        };

        return JsonSerializer.Serialize(payload);
    }
}

public class TeamsFormatter : IWebhookFormatter
{
    private static readonly Dictionary<string, string> EventColors = new()
    {
        ["module.published"] = "00CC00",
        ["module.deleted"] = "CC0000",
        ["module.restored"] = "0076D7",
        ["module.purged"] = "FF8C00",
    };

    public string FormatPayload(WebhookEventData eventData, string? template)
    {
        var themeColor = EventColors.GetValueOrDefault(eventData.Event, "808080");
        var title = $"Module {eventData.Action}: {eventData.Module.Namespace}/{eventData.Module.Name}";
        var summary = title;

        if (!string.IsNullOrWhiteSpace(template))
        {
            var (customTitle, customBody) = TemplateOverrideParser.ParseTemplateOverrides(template, eventData);
            if (customTitle != null) title = customTitle;
            if (customBody != null) summary = customBody;
        }

        var payload = new
        {
            @type = "MessageCard",
            themeColor,
            summary,
            sections = new[]
            {
                new
                {
                    activityTitle = title,
                    facts = new[]
                    {
                        new { name = "Module", value = $"{eventData.Module.Namespace}/{eventData.Module.Name}/{eventData.Module.Provider}" },
                        new { name = "Version", value = eventData.Module.Version },
                        new { name = "Event", value = eventData.Event },
                    },
                },
            },
        };

        return JsonSerializer.Serialize(payload);
    }
}

public class CustomFormatter : IWebhookFormatter
{
    private static readonly GenericFormatter FallbackFormatter = new();

    public string FormatPayload(WebhookEventData eventData, string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return FallbackFormatter.FormatPayload(eventData, template);
        }

        var variables = MustacheRenderer.Flatten(eventData);
        return MustacheRenderer.Render(template, variables);
    }
}

file static class TemplateOverrideParser
{
    public static (string? Title, string? Body) ParseTemplateOverrides(string template, WebhookEventData eventData)
    {
        try
        {
            using var doc = JsonDocument.Parse(template);
            var root = doc.RootElement;
            var variables = MustacheRenderer.Flatten(eventData);

            string? title = null;
            string? body = null;

            if (root.TryGetProperty("title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String)
            {
                title = MustacheRenderer.Render(titleElement.GetString()!, variables);
            }

            if (root.TryGetProperty("body", out var bodyElement) && bodyElement.ValueKind == JsonValueKind.String)
            {
                body = MustacheRenderer.Render(bodyElement.GetString()!, variables);
            }

            return (title, body);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
