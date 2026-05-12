using TerraformRegistry.Models;
using TerraformRegistry.Services.ModuleExtraction;

namespace TerraformRegistry.Tests.UnitTests;

public class ModuleLlmContextGeneratorTests
{
    [Fact]
    public void ModuleLlmContextGeneratorBuildsCompactContextFromExtractionDocument()
    {
        var extraction = new ModuleExtractionDocument
        {
            Readme = new ModuleReadmeDocument
            {
                Title = "AWS VPC Module",
                Markdown = "# AWS VPC Module\n\nCreates AWS VPC networking primitives."
            },
            Inputs = [new ModuleInputDefinition { Name = "name", Type = "string", Required = true, Description = "Name prefix." }],
            Outputs = [new ModuleOutputDefinition { Name = "vpc_id", Description = "VPC id." }],
            ProviderRequirements = [new ModuleProviderRequirement { Name = "aws", Namespace = "hashicorp", VersionConstraint = "~> 5.0" }]
        };

        var module = new TerraformModule
        {
            Id = "hashicorp/vpc/aws/8.0.0",
            Owner = "hashicorp",
            Namespace = "hashicorp",
            Name = "vpc",
            Provider = "aws",
            Version = "8.0.0",
            PublishedAt = "2026-04-29T12:00:00Z",
            Versions = ["8.0.0"],
            Root = "/",
            Submodules = [],
            Providers = new Dictionary<string, string>(StringComparer.Ordinal),
            Description = "Creates AWS VPC networking primitives."
        };

        var generator = new ModuleLlmContextGenerator();

        var context = generator.Generate(module, extraction);

        Assert.Equal("AWS VPC Module", context.Readme.Title);
        Assert.Equal("Creates AWS VPC networking primitives.", context.Summary.OneLine);
        Assert.Single(context.Inputs);
        Assert.Single(context.Providers);
        Assert.Equal("hashicorp", context.Module.Namespace);
    }

    [Fact]
    public void ModuleLlmContextGeneratorUsesConfiguredBaseUrlForGeneratedNavigationLinks()
    {
        var extraction = new ModuleExtractionDocument();
        var module = new TerraformModule
        {
            Id = "acme/network/aws/1.0.0",
            Owner = "acme",
            Namespace = "acme",
            Name = "network",
            Provider = "aws",
            Version = "1.0.0",
            PublishedAt = "2026-04-30T12:00:00Z",
            Versions = ["1.0.0"],
            Root = "/",
            Submodules = [],
            Providers = new Dictionary<string, string>(StringComparer.Ordinal),
            Description = "Network module"
        };

        var generator = new ModuleLlmContextGenerator("https://registry.example.com/");

        var context = generator.Generate(module, extraction);

        Assert.Equal("https://registry.example.com/modules/acme/network/aws", context.Source!.RegistryUrl);
        Assert.Equal("https://registry.example.com/modules/acme/network/aws", context.Navigation.HumanUrl);
        Assert.Equal("https://registry.example.com/v1/llm/modules/acme/network/aws", context.Navigation.ModuleVersionsUrl);
        Assert.Equal("https://registry.example.com/api/admin/module-docs/modules/acme/network/aws/1.0.0", context.Navigation.RawExtractionUrl);
    }
}
