using System.Text.Json;
using Microsoft.Extensions.Options;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services.ModuleExtraction;

public sealed class TerraformConfigInspectRunner : ITerraformModuleInspector
{
    private readonly ExampleDiscoveryService _exampleDiscoveryService;
    private readonly ModuleExtractionOptions _options;
    private readonly IProcessRunner _processRunner;
    private readonly ReadmeDiscoveryService _readmeDiscoveryService;
    private readonly SubmoduleDiscoveryService _submoduleDiscoveryService;

    public TerraformConfigInspectRunner(IProcessRunner processRunner, IOptions<ModuleExtractionOptions> options)
        : this(processRunner, options, new ReadmeDiscoveryService(), new ExampleDiscoveryService(),
            new SubmoduleDiscoveryService())
    {
    }

    public TerraformConfigInspectRunner(
        IProcessRunner processRunner,
        IOptions<ModuleExtractionOptions> options,
        ReadmeDiscoveryService readmeDiscoveryService,
        ExampleDiscoveryService exampleDiscoveryService,
        SubmoduleDiscoveryService submoduleDiscoveryService)
    {
        _processRunner = processRunner;
        _options = options.Value;
        _readmeDiscoveryService = readmeDiscoveryService;
        _exampleDiscoveryService = exampleDiscoveryService;
        _submoduleDiscoveryService = submoduleDiscoveryService;
    }

    public Task<ModuleExtractionDocument> InspectAsync(string modulePath, CancellationToken cancellationToken)
    {
        return LoadAsync(modulePath, cancellationToken);
    }

    public async Task<ModuleExtractionDocument> LoadAsync(string modulePath, CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(
            _options.ToolPath,
            $"--json {QuoteArgument(modulePath)}",
            _options.TimeoutSeconds,
            cancellationToken);

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"terraform-config-inspect failed: {result.StandardError}");

        using var json = JsonDocument.Parse(result.StandardOutput);
        var document = new ModuleExtractionDocument
        {
            GeneratedAt = DateTime.UtcNow,
            Readme = _readmeDiscoveryService.FindPrimary(modulePath),
            Examples = _exampleDiscoveryService.FindExamples(modulePath),
            Submodules = _submoduleDiscoveryService.FindSubmodules(modulePath)
        };

        MapVariables(json.RootElement, document);
        MapOutputs(json.RootElement, document);
        MapProviderRequirements(json.RootElement, document);
        MapResources(json.RootElement, "managed_resources", document.ManagedResources);
        MapResources(json.RootElement, "data_resources", document.DataResources);

        return document;
    }

    private static void MapVariables(JsonElement root, ModuleExtractionDocument document)
    {
        if (!root.TryGetProperty("variables", out var variables) || variables.ValueKind != JsonValueKind.Object)
            return;

        foreach (var variable in variables.EnumerateObject())
        {
            var body = variable.Value;
            document.Inputs.Add(new ModuleInputDefinition
            {
                Name = GetString(body, "name") ?? variable.Name,
                Description = GetString(body, "description"),
                Required = GetBool(body, "required"),
                Type = GetJsonString(body, "type"),
                DefaultJson = body.TryGetProperty("default", out var defaultValue) ? defaultValue.GetRawText() : null,
                Sensitive = GetBool(body, "sensitive")
            });
        }
    }

    private static void MapOutputs(JsonElement root, ModuleExtractionDocument document)
    {
        if (!root.TryGetProperty("outputs", out var outputs) || outputs.ValueKind != JsonValueKind.Object)
            return;

        foreach (var output in outputs.EnumerateObject())
        {
            var body = output.Value;
            document.Outputs.Add(new ModuleOutputDefinition
            {
                Name = GetString(body, "name") ?? output.Name,
                Description = GetString(body, "description"),
                Sensitive = GetBool(body, "sensitive")
            });
        }
    }

    private static void MapProviderRequirements(JsonElement root, ModuleExtractionDocument document)
    {
        if (!root.TryGetProperty("required_providers", out var providers) ||
            providers.ValueKind != JsonValueKind.Object)
            return;

        foreach (var provider in providers.EnumerateObject())
        {
            var body = provider.Value;
            var source = GetString(body, "source") ?? $"hashicorp/{provider.Name}";
            var sourceParts = source.Split('/', StringSplitOptions.RemoveEmptyEntries);

            document.ProviderRequirements.Add(new ModuleProviderRequirement
            {
                Name = provider.Name,
                Namespace = sourceParts.Length > 1 ? sourceParts[0] : "hashicorp",
                Source = source,
                VersionConstraint = GetString(body, "version")
            });
        }
    }

    private static void MapResources(JsonElement root, string propertyName, ICollection<ModuleResourceDefinition> target)
    {
        if (!root.TryGetProperty(propertyName, out var resources) || resources.ValueKind != JsonValueKind.Object)
            return;

        foreach (var resource in resources.EnumerateObject())
        {
            var body = resource.Value;
            target.Add(new ModuleResourceDefinition
            {
                Type = GetString(body, "type") ?? resource.Name.Split('.', 2)[0],
                Name = GetString(body, "name") ?? resource.Name,
                Provider = GetString(body, "provider"),
                Mode = GetString(body, "mode")
            });
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string? GetJsonString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.GetRawText();
    }

    private static bool GetBool(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.True;
    }

    private static string QuoteArgument(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }
}
