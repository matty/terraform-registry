using TerraformRegistry.Services.ModuleExtraction;

namespace TerraformRegistry.Tests.Support;

public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly int _exitCode;
    private readonly string _standardOutput;
    private readonly string _standardError;

    public FakeProcessRunner(int exitCode, string standardOutput, string standardError = "")
    {
        _exitCode = exitCode;
        _standardOutput = standardOutput;
        _standardError = standardError;
    }

    public string? FileName { get; private set; }

    public string? Arguments { get; private set; }

    public Task<ProcessResult> RunAsync(string fileName, string arguments, int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        FileName = fileName;
        Arguments = arguments;
        return Task.FromResult(new ProcessResult(_exitCode, _standardOutput, _standardError));
    }
}
