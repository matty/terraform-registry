using System.Text.RegularExpressions;

namespace TerraformRegistry.Services;

public static partial class MustacheRenderer
{
    [GeneratedRegex(@"\{\{(\w+(?:\.\w+)*)\}\}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex VariablePattern();

    public static string Render(string template, Dictionary<string, string> variables)
    {
        return VariablePattern().Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            return variables.TryGetValue(key, out var value) ? value : string.Empty;
        });
    }

    public static Dictionary<string, string> Flatten(WebhookEventData data)
    {
        return new Dictionary<string, string>
(StringComparer.Ordinal)
        {
            ["id"] = data.Id,
            ["event"] = data.Event,
            ["action"] = data.Action,
            ["timestamp"] = data.Timestamp,
            ["module.namespace"] = data.Module.Namespace,
            ["module.name"] = data.Module.Name,
            ["module.provider"] = data.Module.Provider,
            ["module.version"] = data.Module.Version,
            ["module.description"] = data.Module.Description ?? string.Empty,
            ["module.source"] = data.Module.Source,
            ["module.download_url"] = data.Module.DownloadUrl,
        };
    }
}
