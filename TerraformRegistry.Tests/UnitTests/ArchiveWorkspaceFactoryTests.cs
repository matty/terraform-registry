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
    public async Task ArchiveWorkspaceFactoryExtractsEmptyRegularFileFromTarGz()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        var tarGzPath = Path.Combine(tempDir.FullName, "module.tar.gz");
        await TestArchiveBuilder.CreateTarGzAsync(tarGzPath, ("module/main.tf", string.Empty));

        await using var stream = File.OpenRead(tarGzPath);
        var factory = new ArchiveWorkspaceFactory(new ModuleExtractionOptions
        {
            TempRoot = Path.Combine(tempDir.FullName, "workspaces")
        });

        await using var workspace = await factory.CreateAsync(stream, CancellationToken.None);

        var file = Path.Combine(workspace.RootPath, "main.tf");
        Assert.True(File.Exists(file));
        Assert.Equal(0, new FileInfo(file).Length);
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

    [Fact]
    public async Task ArchiveWorkspaceFactoryRejectsExpandedContentOverConfiguredLimit()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        await using var stream = new MemoryStream(TestArchiveBuilder.CreateZipBytes(("module/main.tf", "0123456789")));
        var workspacesRoot = tempDir.CreateSubdirectory("workspaces").FullName;
        var factory = new ArchiveWorkspaceFactory(new ModuleExtractionOptions
        {
            TempRoot = workspacesRoot,
            MaxExpandedArchiveBytes = 4
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            factory.CreateAsync(stream, CancellationToken.None));

        Assert.Contains("expanded", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(!Directory.Exists(workspacesRoot) || !Directory.EnumerateDirectories(workspacesRoot).Any());
    }

    [Fact]
    public async Task ArchiveWorkspaceFactoryRejectsPathTraversalAndCleansWorkspace()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        await using var stream = new MemoryStream(TestArchiveBuilder.CreateZipBytes(("../outside.tf", "resource {}")));
        var factory = new ArchiveWorkspaceFactory(new ModuleExtractionOptions
        {
            TempRoot = Path.Combine(tempDir.FullName, "workspaces")
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            factory.CreateAsync(stream, CancellationToken.None));

        Assert.Contains("escapes", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(tempDir.FullName, "outside.tf")));
        var workspacesRoot = Path.Combine(tempDir.FullName, "workspaces");
        Assert.True(!Directory.Exists(workspacesRoot) || !Directory.EnumerateDirectories(workspacesRoot).Any());
    }

    [Fact]
    public async Task ArchiveWorkspaceFactoryRejectsArchivesOverConfiguredEntryCount()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        await using var stream = new MemoryStream(TestArchiveBuilder.CreateZipBytes(
            ("module/one.tf", "one"),
            ("module/two.tf", "two")));
        var factory = new ArchiveWorkspaceFactory(new ModuleExtractionOptions
        {
            TempRoot = Path.Combine(tempDir.FullName, "workspaces"),
            MaxArchiveEntries = 1
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            factory.CreateAsync(stream, CancellationToken.None));

        Assert.Contains("entry count", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ArchiveWorkspaceFactoryRejectsEntryOverConfiguredExpandedEntryLimit()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        await using var stream = new MemoryStream(TestArchiveBuilder.CreateZipBytes(("module/main.tf", "0123456789")));
        var workspacesRoot = tempDir.CreateSubdirectory("workspaces").FullName;
        var factory = new ArchiveWorkspaceFactory(new ModuleExtractionOptions
        {
            TempRoot = workspacesRoot,
            MaxExpandedEntryBytes = 4
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            factory.CreateAsync(stream, CancellationToken.None));

        Assert.Contains("entry", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateDirectories(workspacesRoot));
    }

    [Fact]
    public async Task ArchiveWorkspaceFactoryRejectsCompressionBomb()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        await using var stream = new MemoryStream(TestArchiveBuilder.CreateZipBytes(("module/main.tf", new string('a', 16_384))));
        var workspacesRoot = tempDir.CreateSubdirectory("workspaces").FullName;
        var factory = new ArchiveWorkspaceFactory(new ModuleExtractionOptions
        {
            TempRoot = workspacesRoot,
            MaxCompressionRatio = 2
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            factory.CreateAsync(stream, CancellationToken.None));

        Assert.Contains("compression ratio", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateDirectories(workspacesRoot));
    }

    [Fact]
    public async Task ArchiveWorkspaceFactoryRejectsCorruptArchiveAndCleansWorkspace()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        await using var stream = new MemoryStream([0x1F, 0x8B, 0x08, 0x00, 0xFF]);
        var workspacesRoot = tempDir.CreateSubdirectory("workspaces").FullName;
        var factory = new ArchiveWorkspaceFactory(new ModuleExtractionOptions { TempRoot = workspacesRoot });

        await Assert.ThrowsAnyAsync<IOException>(() => factory.CreateAsync(stream, CancellationToken.None));

        Assert.True(!Directory.Exists(workspacesRoot) || !Directory.EnumerateDirectories(workspacesRoot).Any());
    }

    [Fact]
    public async Task ArchiveWorkspaceFactoryRejectsTruncatedArchiveAndCleansWorkspace()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        var archive = TestArchiveBuilder.CreateZipBytes(("module/main.tf", "resource {}"));
        await using var stream = new MemoryStream(archive[..^4]);
        var workspacesRoot = tempDir.CreateSubdirectory("workspaces").FullName;
        var factory = new ArchiveWorkspaceFactory(new ModuleExtractionOptions { TempRoot = workspacesRoot });

        await Assert.ThrowsAnyAsync<InvalidDataException>(() => factory.CreateAsync(stream, CancellationToken.None));

        Assert.True(!Directory.Exists(workspacesRoot) || !Directory.EnumerateDirectories(workspacesRoot).Any());
    }

    [Fact]
    public async Task ArchiveWorkspaceFactoryCleansWorkspaceWhenRequestIsCancelled()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        await using var stream = new MemoryStream(TestArchiveBuilder.CreateZipBytes(("module/main.tf", "resource {}")));
        var workspacesRoot = tempDir.CreateSubdirectory("workspaces").FullName;
        var factory = new ArchiveWorkspaceFactory(new ModuleExtractionOptions { TempRoot = workspacesRoot });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => factory.CreateAsync(stream, cancellation.Token));

        Assert.True(!Directory.Exists(workspacesRoot) || !Directory.EnumerateDirectories(workspacesRoot).Any());
    }
}
