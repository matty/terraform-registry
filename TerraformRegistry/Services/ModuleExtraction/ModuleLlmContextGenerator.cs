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

    public ModuleLlmContextDocument Generate(Module module, ModuleExtractionDocument extraction)
    {
        var modulePath = $"/modules/{module.Namespace}/{module.Name}/{module.Provider}";
        var moduleVersionsPath = $"/v1/llm/modules/{module.Namespace}/{module.Name}/{module.Provider}";
        var rawExtractionPath = $"/api/admin/module-docs/modules/{module.Namespace}/{module.Name}/{module.Provider}/{module.Version}";

        return new ModuleLlmContextDocument
        {
            Module = new ModuleLlmModuleReference
            {
                Namespace = module.Namespace,
                Name = module.Name,
                Provider = module.Provider,
                Version = module.Version
            },
            Source = new ModuleLlmSourceReference
            {
                RegistryUrl = BuildAbsoluteUrl(modulePath),
                PublishedAt = module.PublishedAt
            },
            Summary = new ModuleLlmContextSummary
            {
                OneLine = BuildOneLineSummary(module, extraction),
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

    private static string? BuildOneLineSummary(Module module, ModuleExtractionDocument extraction)
    {
        if (!string.IsNullOrWhiteSpace(module.Description))
            return module.Description.Trim();

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
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#", StringComparison.Ordinal))
            .ToList();

        if (lines.Count == 0)
            return null;

        var paragraph = string.Join(" ", lines);
        paragraph = MarkdownWhitespaceRegex().Replace(paragraph, " ").Trim();

        return paragraph.Length == 0 ? null : paragraph;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex MarkdownWhitespaceRegex();
}
