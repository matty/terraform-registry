using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TerraformRegistry.API;
using TerraformRegistry.Models;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class ProviderReleaseFixtureTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    private const string AuthToken = "default-auth-token";

    [Fact]
    public async Task SignedFixtureProviderReleaseCanBePublishedAndServedByProtocolEndpoints()
    {
        var hasZipTool = await CommandExistsAsync("zip") || await CommandExistsAsync("python3");
        if (!await CommandExistsAsync("gpg") || !hasZipTool)
        {
            Output.WriteLine("Skipping signed provider fixture test because gpg and a zip tool are unavailable.");
            return;
        }

        var releaseDir = Path.Combine(Path.GetTempPath(), $"provider-release-{Guid.NewGuid():N}");
        Directory.CreateDirectory(releaseDir);

        try
        {
            var scriptPath = Path.Combine(
                AppContext.BaseDirectory,
                "TestData",
                "provider-release",
                "create-test-provider-release.sh");
            var result = await RunProcessAsync("bash", [scriptPath, releaseDir], null, captureOutput: true);
            Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);

            var publisher = await CreateClientWithPermissionsAsync(
                "provider-publisher@example.com",
                "provider-publisher",
                [Permissions.ProvidersRead, Permissions.ProvidersPublish, Permissions.ProvidersKeysManage]);

            await PublishFixtureProviderAsync(publisher, releaseDir);

            var versions = await publisher.GetAsync("/v1/providers/acme/example/versions");
            Assert.Equal(HttpStatusCode.OK, versions.StatusCode);

            var package = await publisher.GetAsync("/v1/providers/acme/example/1.0.0/download/linux/amd64");
            Assert.Equal(HttpStatusCode.OK, package.StatusCode);

            using var json = JsonDocument.Parse(await package.Content.ReadAsStringAsync());
            var root = json.RootElement;
            Assert.Equal("terraform-provider-example_1.0.0_linux_amd64.zip", root.GetProperty("filename").GetString());
            Assert.True(root.TryGetProperty("download_url", out var downloadUrl));
            Assert.True(root.TryGetProperty("shasums_url", out var shasumsUrl));
            Assert.True(root.TryGetProperty("shasums_signature_url", out var signatureUrl));
            Assert.True(root.TryGetProperty("signing_keys", out _));

            Assert.Equal(HttpStatusCode.OK, (await publisher.GetAsync(downloadUrl.GetString())).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await publisher.GetAsync(shasumsUrl.GetString())).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await publisher.GetAsync(signatureUrl.GetString())).StatusCode);
        }
        finally
        {
            if (Directory.Exists(releaseDir))
            {
                Directory.Delete(releaseDir, true);
            }
        }
    }

    private static async Task PublishFixtureProviderAsync(HttpClient publisher, string releaseDir)
    {
        var keyId = (await File.ReadAllTextAsync(Path.Combine(releaseDir, "key-id.txt"))).Trim();
        var publicKey = await File.ReadAllTextAsync(Path.Combine(releaseDir, "public-key.asc"));
        var packagePath = Path.Combine(releaseDir, "terraform-provider-example_1.0.0_linux_amd64.zip");
        var shasumsPath = Path.Combine(releaseDir, "terraform-provider-example_1.0.0_SHA256SUMS");
        var signaturePath = Path.Combine(releaseDir, "terraform-provider-example_1.0.0_SHA256SUMS.sig");
        var shasum = (await File.ReadAllTextAsync(shasumsPath)).Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

        Assert.Equal(HttpStatusCode.Created, (await publisher.PostAsJsonAsync("/api/providers", new CreateProviderRequest
        {
            Namespace = "acme",
            Type = "example",
            DisplayName = "Example"
        })).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await publisher.PostAsJsonAsync("/api/providers/acme/example/gpg-keys",
            new CreateProviderGpgKeyRequest
            {
                KeyId = keyId,
                AsciiArmor = publicKey,
                Source = "fixture"
            })).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await publisher.PostAsJsonAsync("/api/providers/acme/example/versions",
            new CreateProviderVersionRequest
            {
                Version = "1.0.0",
                Protocols = ["5.0"],
                KeyId = keyId
            })).StatusCode);

        await using (var shasums = File.OpenRead(shasumsPath))
        using (var content = new StreamContent(shasums))
        {
            Assert.Equal(HttpStatusCode.NoContent, (await publisher.PutAsync("/api/providers/acme/example/versions/1.0.0/shasums", content)).StatusCode);
        }

        await using (var signature = File.OpenRead(signaturePath))
        using (var content = new StreamContent(signature))
        {
            Assert.Equal(HttpStatusCode.NoContent, (await publisher.PutAsync("/api/providers/acme/example/versions/1.0.0/shasums.sig", content)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Created, (await publisher.PostAsJsonAsync("/api/providers/acme/example/versions/1.0.0/platforms",
            new CreateProviderPlatformRequest
            {
                Os = "linux",
                Arch = "amd64",
                Filename = Path.GetFileName(packagePath),
                Shasum = shasum
            })).StatusCode);

        await using (var package = File.OpenRead(packagePath))
        using (var content = new StreamContent(package))
        {
            Assert.Equal(HttpStatusCode.NoContent,
                (await publisher.PutAsync("/api/providers/acme/example/versions/1.0.0/platforms/linux/amd64/package", content)).StatusCode);
        }
    }

    private static async Task<bool> CommandExistsAsync(string command)
    {
        try
        {
            var result = await RunProcessAsync(command, ["--version"], null, captureOutput: false);
            return result.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        bool captureOutput)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = captureOutput,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var outputTask = captureOutput ? process.StandardOutput.ReadToEndAsync() : Task.FromResult(string.Empty);
        var errorTask = captureOutput ? process.StandardError.ReadToEndAsync() : Task.FromResult(string.Empty);
        await process.WaitForExitAsync();

        return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
