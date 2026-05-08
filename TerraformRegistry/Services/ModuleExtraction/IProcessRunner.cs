namespace TerraformRegistry.Services.ModuleExtraction;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, string arguments, int timeoutSeconds,
        CancellationToken cancellationToken);
}

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
