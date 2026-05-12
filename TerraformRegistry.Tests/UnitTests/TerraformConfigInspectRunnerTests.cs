using Microsoft.Extensions.Options;
using TerraformRegistry.Services;
using TerraformRegistry.Services.ModuleExtraction;
using TerraformRegistry.Tests.Support;

namespace TerraformRegistry.Tests.UnitTests;

public class TerraformConfigInspectRunnerTests
{
    [Fact]
    public async Task TerraformConfigInspectRunnerMapsJsonIntoExtractionDocument()
    {
        var json = """
        {
          "variables": {
            "name": {
              "name": "name",
              "description": "Name prefix.",
              "required": true,
              "type": "string"
            }
          },
          "outputs": {
            "vpc_id": {
              "name": "vpc_id",
              "description": "Created VPC ID."
            }
          },
          "required_providers": {
            "aws": {
              "source": "hashicorp/aws",
              "version": "~> 5.0"
            }
          }
        }
        """;

        var runner = new TerraformConfigInspectRunner(
            new FakeProcessRunner(0, json),
            Options.Create(new ModuleExtractionOptions()));

        var document = await runner.InspectAsync("/tmp/module", CancellationToken.None);

        Assert.Single(document.Inputs);
        Assert.Equal("name", document.Inputs[0].Name);
        Assert.Equal("string", document.Inputs[0].Type);
        Assert.Single(document.Outputs);
        Assert.Equal("vpc_id", document.Outputs[0].Name);
        Assert.Single(document.ProviderRequirements);
        Assert.Equal("aws", document.ProviderRequirements[0].Name);
        Assert.Equal("hashicorp", document.ProviderRequirements[0].Namespace);
        Assert.Equal("hashicorp/aws", document.ProviderRequirements[0].Source);
        Assert.Equal("~> 5.0", document.ProviderRequirements[0].VersionConstraint);
    }
}
