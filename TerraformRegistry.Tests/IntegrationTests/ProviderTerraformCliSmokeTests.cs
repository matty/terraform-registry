using System.Diagnostics;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public sealed class ProviderTerraformCliSmokeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task TerraformInit_InstallsSignedProviderFromLocalRegistry_WhenTerraformCliIsAvailable()
    {
        var require = IsTruthy(Environment.GetEnvironmentVariable("TF_REGISTRY_REQUIRE_TERRAFORM_CLI_TEST"));
        var terraformExists = await CommandExistsAsync("terraform");
        var run = require || IsTruthy(Environment.GetEnvironmentVariable("TF_REGISTRY_RUN_TERRAFORM_CLI_TEST")) ||
                  terraformExists;

        if (!run)
        {
            output.WriteLine("Skipping Terraform CLI smoke test. Set TF_REGISTRY_RUN_TERRAFORM_CLI_TEST=1 to enable it.");
            return;
        }

        if (!terraformExists && !require)
        {
            output.WriteLine("Skipping Terraform CLI smoke test because terraform is not installed.");
            return;
        }

        if (!await CommandExistsAsync("gpg") || !await CommandExistsAsync("python3"))
        {
            Assert.True(!require, "gpg and python3 are required when TF_REGISTRY_REQUIRE_TERRAFORM_CLI_TEST=1.");
            output.WriteLine("Skipping Terraform CLI smoke test because gpg and python3 are not installed.");
            return;
        }

        var repoRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(repoRoot, "devutils", "provider-registry-terraform-smoke-test.sh");
        Assert.True(File.Exists(scriptPath), $"Terraform CLI smoke script was not found at {scriptPath}.");

        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (!terraformExists && require)
        {
            environment["TF_REG_SMOKE_AUTO_INSTALL_TERRAFORM"] = "1";
        }

        var result = await RunProcessAsync("bash", [scriptPath], repoRoot, TimeSpan.FromMinutes(5), environment);

        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
    }

    private static bool IsTruthy(string? value) =>
        bool.TryParse(value, out var parsed) && parsed || string.Equals(value, "1", StringComparison.Ordinal);

    private static async Task<bool> CommandExistsAsync(string command)
    {
        var result = await RunProcessAsync("bash", ["-lc", $"command -v {command}"], Directory.GetCurrentDirectory(), TimeSpan.FromSeconds(10));
        return result.ExitCode == 0;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "devutils")) &&
                File.Exists(Path.Combine(directory.FullName, "terraform-registry.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        using var process = new Process();
        process.StartInfo.FileName = fileName;
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.StartInfo.WorkingDirectory = workingDirectory;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        if (environment != null)
        {
            foreach (var (key, value) in environment)
            {
                if (value == null)
                {
                    process.StartInfo.Environment.Remove(key);
                }
                else
                {
                    process.StartInfo.Environment[key] = value;
                }
            }
        }

        process.Start();
        using var cts = new CancellationTokenSource(timeout);

        try
        {
            var standardOutput = process.StandardOutput.ReadToEndAsync(cts.Token);
            var standardError = process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);
            return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Process may have exited between timeout and kill.
            }

            return new ProcessResult(-1, string.Empty, $"Process timed out after {timeout}.");
        }
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
