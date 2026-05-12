using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services.ModuleExtraction;

public sealed partial class ModuleLlmContextGenerator : IModuleLlmContextGenerator
{
    private readonly string? _baseUrl;

    public ModuleLlmContextGenerator()
    {
    }

    public ModuleLlmContextGenerator(IConfiguration configuration)
        : this(configuration["BaseUrl"])
    {
    }

    public ModuleLlmContextGenerator(string? baseUrl)
    {
        _baseUrl = NormalizeBaseUrl(baseUrl);
    }

    public ModuleLlmContextDocument Generate(TerraformModule terraformModule, ModuleExtractionDocument extraction)
    {
        var modulePath = $"/modules/{terraformModule.Namespace}/{terraformModule.Name}/{terraformModule.Provider}";
        var moduleVersionsPath = $"/v1/llm/modules/{terraformModule.Namespace}/{terraformModule.Name}/{terraformModule.Provider}";
        var rawExtractionPath = $"/api/admin/module-docs/modules/{terraformModule.Namespace}/{terraformModule.Name}/{terraformModule.Provider}/{terraformModule.Version}";

        return new ModuleLlmContextDocument
        {
            Module = new ModuleLlmModuleReference
            {
                Namespace = terraformModule.Namespace,
                Name = terraformModule.Name,
                Provider = terraformModule.Provider,
                Version = terraformModule.Version
            },
            Source = new ModuleLlmSourceReference
            {
                RegistryUrl = BuildAbsoluteUrl(modulePath),
                PublishedAt = terraformModule.PublishedAt
            },
            Summary = new ModuleLlmContextSummary
            {
                OneLine = BuildOneLineSummary(terraformModule, extraction),
                Capabilities = BuildCapabilities(extraction)
            },
            Inputs = extraction.Inputs,
            Outputs = extraction.Outputs,
            Providers = extraction.ProviderRequirements,
            Resources = new ModuleLlmResourceSummary
            {
                Managed = extraction.ManagedResources.Select(resource => resource.Type).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToList()!,
                Data = extraction.DataResources.Select(resource => resource.Type).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToList()!
            },
            Examples = extraction.Examples.Select(example => new ModuleLlmExampleSummary
            {
                Name = example.Name,
                Path = example.Path
            }).ToList(),
            Readme = new ModuleLlmReadmeSummary
            {
                Title = extraction.Readme?.Title,
                Summary = ExtractFirstParagraph(extraction.Readme?.Markdown)
            },
            Navigation = new ModuleLlmNavigationLinks
            {
                HumanUrl = BuildAbsoluteUrl(modulePath),
                ModuleVersionsUrl = BuildAbsoluteUrl(moduleVersionsPath),
                RawExtractionUrl = BuildAbsoluteUrl(rawExtractionPath)
            }
        };
    }

    private string BuildAbsoluteUrl(string path)
    {
        return _baseUrl == null ? path : $"{_baseUrl}{path}";
    }

    private static string? NormalizeBaseUrl(string? baseUrl)
    {
        return string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.TrimEnd('/');
    }

    private static string? BuildOneLineSummary(TerraformModule terraformModule, ModuleExtractionDocument extraction)
    {
        if (!string.IsNullOrWhiteSpace(terraformModule.Description))
            return terraformModule.Description.Trim();

        if (!string.IsNullOrWhiteSpace(extraction.Readme?.Title))
            return ExtractFirstParagraph(extraction.Readme.Markdown) ?? extraction.Readme.Title.Trim();

        return null;
    }

    private static List<string> BuildCapabilities(ModuleExtractionDocument extraction)
    {
        var capabilities = new List<string>();

        if (extraction.ManagedResources.Count > 0)
            capabilities.Add($"Manages {extraction.ManagedResources.Count} Terraform resource types");

        if (extraction.Examples.Count > 0)
            capabilities.Add($"Includes {extraction.Examples.Count} example configurations");

        return capabilities;
    }

    private static string? ExtractFirstParagraph(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return null;

        var lines = markdown
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
            .ToList();

        if (lines.Count == 0)
            return null;

        var paragraph = string.Join(" ", lines);
        paragraph = MarkdownWhitespaceRegex().Replace(paragraph, " ").Trim();

        return paragraph.Length == 0 ? null : paragraph;
    }

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MarkdownWhitespaceRegex();
}
