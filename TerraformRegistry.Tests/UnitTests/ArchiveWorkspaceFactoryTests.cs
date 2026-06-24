using TerraformRegistry.Services;
using TerraformRegistry.Services.ModuleExtraction;
using TerraformRegistry.Tests.Support;

namespace TerraformRegistry.Tests.UnitTests;

public class ArchiveWorkspaceFactoryTests
{
    [Fact]
    public async Task ArchiveWorkspaceFactoryExtractsGitHubTarballDespiteZipStorageName()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        var tarGzPath = Path.Combine(tempDir.FullName, "artifact.zip");

        await TestArchiveBuilder.CreateTarGzAsync(
            tarGzPath,
            ("repo-123456/README.md", "# Example"),
            ("repo-123456/variables.tf", "variable \"name\" { type = string }"));

        await using var stream = File.OpenRead(tarGzPath);
        var factory = new ArchiveWorkspaceFactory(new ModuleExtractionOptions
        {
            TempRoot = Path.Combine(tempDir.FullName, "workspaces")
        });

        await using var workspace = await factory.CreateAsync(stream, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(workspace.RootPath, "README.md")));
        Assert.True(File.Exists(Path.Combine(workspace.RootPath, "variables.tf")));
    }

    [Fact]
    public async Task ArchiveWorkspaceFactoryRejectsArchivesOverConfiguredLimit()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        await using var stream = new MemoryStream([1, 2, 3, 4, 5]);
        var factory = new ArchiveWorkspaceFactory(new ModuleExtractionOptions
        {
            TempRoot = Path.Combine(tempDir.FullName, "workspaces"),
            MaxArchiveBytes = 4
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            factory.CreateAsync(stream, CancellationToken.None));

        Assert.Contains("exceeds", ex.Message, StringComparison.OrdinalIgnoreCase);
        var workspacesRoot = Path.Combine(tempDir.FullName, "workspaces");
        Assert.True(!Directory.Exists(workspacesRoot) || !Directory.EnumerateDirectories(workspacesRoot).Any());
    }
}
