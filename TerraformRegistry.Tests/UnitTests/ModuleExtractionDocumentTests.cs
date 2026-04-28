using System.Text.Json;
using TerraformRegistry.Models;

namespace TerraformRegistry.Tests.UnitTests;

public class ModuleExtractionDocumentTests
{
    [Fact]
    public void ModuleExtractionDocumentRoundTripsReadmeInputsOutputsAndExamples()
    {
        var document = new ModuleExtractionDocument
        {
            SchemaVersion = "module-extraction.v1",
            GeneratedAt = new DateTime(2026, 4, 27, 12, 0, 0, DateTimeKind.Utc),
            Readme = new ModuleReadmeDocument
            {
                Path = "README.md",
                Title = "Network Module",
                Markdown = "# Network Module\n\nCreates a VPC."
            },
            Inputs =
            [
                new ModuleInputDefinition
                {
                    Name = "name",
                    Description = "Name prefix.",
                    Required = true,
                    Type = "string"
                }
            ],
            Outputs =
            [
                new ModuleOutputDefinition
                {
                    Name = "vpc_id",
                    Description = "Created VPC ID."
                }
            ],
            Examples =
            [
                new ModuleExampleDefinition
                {
                    Name = "basic",
                    Path = "examples/basic",
                    ReadmePath = "examples/basic/README.md"
                }
            ]
        };

        var json = JsonSerializer.Serialize(document);
        var roundTripped = JsonSerializer.Deserialize<ModuleExtractionDocument>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal("README.md", roundTripped!.Readme!.Path);
        Assert.Equal("name", roundTripped.Inputs.Single().Name);
        Assert.Equal("vpc_id", roundTripped.Outputs.Single().Name);
        Assert.Equal("examples/basic", roundTripped.Examples.Single().Path);
    }

    [Fact]
    public void ModuleArtifactMetadataCanStoreDocumentationSummary()
    {
        var metadata = new ModuleArtifactMetadata
        {
            Documentation = new ModuleDocumentationSummary
            {
                PrimaryReadmePath = "README.md",
                InputCount = 2,
                OutputCount = 1,
                ExampleCount = 3,
                HasSubmoduleDocs = true
            }
        };

        Assert.Equal("README.md", metadata.Documentation!.PrimaryReadmePath);
        Assert.Equal(3, metadata.Documentation.ExampleCount);
        Assert.True(metadata.Documentation.HasSubmoduleDocs);
    }
}
