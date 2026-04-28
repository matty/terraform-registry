using TerraformRegistry.Services.ModuleExtraction;

namespace TerraformRegistry.Tests.UnitTests;

public class ReadmeDiscoveryServiceTests
{
    [Fact]
    public void ReadmeDiscoveryService_PrefersRootReadmeAndFindsExamples()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(tempDir.FullName, "README.md"), "# Network Module");
        Directory.CreateDirectory(Path.Combine(tempDir.FullName, "examples", "basic"));
        File.WriteAllText(Path.Combine(tempDir.FullName, "examples", "basic", "README.md"), "# Basic Example");

        var readme = new ReadmeDiscoveryService().FindPrimary(tempDir.FullName);
        var examples = new ExampleDiscoveryService().FindExamples(tempDir.FullName);

        Assert.NotNull(readme);
        Assert.Equal("README.md", readme!.Path);
        Assert.Equal("Network Module", readme.Title);
        Assert.Single(examples);
        Assert.Equal("basic", examples[0].Name);
        Assert.Equal("examples/basic", examples[0].Path);
        Assert.Equal("examples/basic/README.md", examples[0].ReadmePath);
    }
}
