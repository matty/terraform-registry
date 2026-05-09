using System.Text.Json;
using TerraformRegistry.Models;

namespace TerraformRegistry.Tests.UnitTests;

public class ModuleLlmContextDocumentTests
{
    [Fact]
    public void ModuleLlmContextDocumentRoundTripsCoreSections()
    {
        var document = new ModuleLlmContextDocument
        {
            Module = new ModuleLlmModuleReference
            {
                Namespace = "hashicorp",
                Name = "vpc",
                Provider = "aws",
                Version = "6.0.0"
            },
            Summary = new ModuleLlmContextSummary
            {
                OneLine = "Creates AWS VPC networking primitives.",
                Capabilities = ["Creates a VPC"]
            },
            Navigation = new ModuleLlmNavigationLinks
            {
                HumanUrl = "https://registry.example.com/modules/hashicorp/vpc/aws/6.0.0"
            }
        };

        var json = JsonSerializer.Serialize(document);
        var roundTripped = JsonSerializer.Deserialize<ModuleLlmContextDocument>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal("hashicorp", roundTripped!.Module.Namespace);
        Assert.Equal("Creates a VPC", roundTripped.Summary.Capabilities.Single());
        Assert.Equal("https://registry.example.com/modules/hashicorp/vpc/aws/6.0.0", roundTripped.Navigation.HumanUrl);
    }

    [Fact]
    public void ModuleArtifactMetadataCanStoreLlmContextState()
    {
        var metadata = new ModuleArtifactMetadata
        {
            LlmContext = new ModuleLlmContextState
            {
                Status = "succeeded",
                LastSucceededAt = new DateTime(2026, 4, 29, 12, 0, 0, DateTimeKind.Utc)
            }
        };

        Assert.Equal("succeeded", metadata.LlmContext.Status);
        Assert.NotNull(metadata.LlmContext.LastSucceededAt);
    }
}
