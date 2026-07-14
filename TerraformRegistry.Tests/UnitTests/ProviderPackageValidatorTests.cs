using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using TerraformRegistry.Services;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.UnitTests;

public class ProviderPackageValidatorTests
{
    private readonly ITestOutputHelper _output;

    public ProviderPackageValidatorTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ExpectedProviderPackageFilenameReturnsTerraformRegistryFilename()
    {
        Assert.Equal(
            "terraform-provider-example_1.0.0_linux_amd64.zip",
            ProviderPackageValidator.ExpectedProviderPackageFilename("example", "1.0.0", "linux", "amd64"));
    }

    [Fact]
    public async Task ValidatePackageSha256AsyncReturnsExpectedHash()
    {
        await using var stream = new MemoryStream([1, 2, 3]);

        var shasum = await ProviderPackageValidator.ComputeSha256HexAsync(stream, CancellationToken.None);

        Assert.Equal("039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81", shasum);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void ParseShasumsFindsExactFilename()
    {
        const string shasums = "abc123  terraform-provider-example_1.0.0_linux_amd64.zip\n";

        var parsed = ProviderPackageValidator.ParseShasums(shasums);

        Assert.Equal("abc123", parsed["terraform-provider-example_1.0.0_linux_amd64.zip"]);
    }

    [Fact]
    public void ValidatePackageMetadataRejectsMismatchedFilename()
    {
        var result = ProviderPackageValidator.ValidatePackageMetadata(
            "example",
            "1.0.0",
            "linux",
            "amd64",
            "wrong.zip",
            "abc123",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["wrong.zip"] = "abc123" });

        Assert.False(result.Valid);
        Assert.Contains("filename", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidatePackageMetadataRejectsShasumsWithoutFilename()
    {
        var result = ProviderPackageValidator.ValidatePackageMetadata(
            "example",
            "1.0.0",
            "linux",
            "amd64",
            "terraform-provider-example_1.0.0_linux_amd64.zip",
            "abc123",
            new Dictionary<string, string>(StringComparer.Ordinal));

        Assert.False(result.Valid);
        Assert.Contains("SHA256SUMS", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidatePackageAsyncVerifiesDetachedSignatureFromNonSeekableStorageStreamWhenGpgIsAvailable()
    {
        if (!await CommandSucceedsAsync("gpg", "--version", null))
        {
            _output.WriteLine("gpg is not available; skipping OpenPGP fixture path.");
            return;
        }

        using var temp = new TempDirectory();
        var gpgHome = Path.Combine(temp.Path, "gnupg");
        Directory.CreateDirectory(gpgHome);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(gpgHome, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var packageBytes = Encoding.UTF8.GetBytes("provider package");
        var shasum = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();
        const string filename = "terraform-provider-example_1.0.0_linux_amd64.zip";
        var shasumsText = $"{shasum}  {filename}\n";
        var shasumsPath = Path.Combine(temp.Path, "SHA256SUMS");
        var signaturePath = Path.Combine(temp.Path, "SHA256SUMS.sig");
        await File.WriteAllTextAsync(shasumsPath, shasumsText);

        var commonArgs = $"--homedir \"{gpgHome}\" --batch --pinentry-mode loopback --passphrase \"\"";
        Assert.True(await CommandSucceedsAsync("gpg", $"{commonArgs} --quick-generate-key \"Provider Test <provider@example.com>\" rsa2048 sign 1d", temp.Path));
        Assert.True(await CommandSucceedsAsync("gpg", $"{commonArgs} --detach-sign --output \"{signaturePath}\" \"{shasumsPath}\"", temp.Path));

        var publicKey = await CaptureCommandAsync("gpg", $"--homedir \"{gpgHome}\" --armor --export provider@example.com", temp.Path);
        var signatureBytes = await File.ReadAllBytesAsync(signaturePath);
        await using var package = new MemoryStream(packageBytes);
        await using var shasums = new MemoryStream(Encoding.UTF8.GetBytes(shasumsText));
        await using var signature = new NonSeekableReadStream(signatureBytes);
        var validator = new ProviderPackageValidator();

        var result = await validator.ValidatePackageAsync(
            "example",
            "1.0.0",
            "linux",
            "amd64",
            filename,
            shasum,
            package,
            shasums,
            signature,
            publicKey,
            CancellationToken.None);

        Assert.True(result.Valid, result.Error);

        signatureBytes[^1] ^= 0x01;
        await using var tamperedPackage = new MemoryStream(packageBytes);
        await using var tamperedShasums = new MemoryStream(Encoding.UTF8.GetBytes(shasumsText));
        await using var tamperedSignature = new NonSeekableReadStream(signatureBytes);

        var tamperedResult = await validator.ValidatePackageAsync(
            "example",
            "1.0.0",
            "linux",
            "amd64",
            filename,
            shasum,
            tamperedPackage,
            tamperedShasums,
            tamperedSignature,
            publicKey,
            CancellationToken.None);

        Assert.False(tamperedResult.Valid);
    }

    [Fact]
    public async Task ValidatePackageAsyncPropagatesCallerCancellationWhileReadingSignature()
    {
        var packageBytes = Encoding.UTF8.GetBytes("provider package");
        var shasum = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();
        const string filename = "terraform-provider-example_1.0.0_linux_amd64.zip";
        var shasumsText = $"{shasum}  {filename}\n";
        using var cancellation = new CancellationTokenSource();
        await using var package = new MemoryStream(packageBytes);
        await using var shasums = new MemoryStream(Encoding.UTF8.GetBytes(shasumsText));
        await using var signature = new CancellationAwareReadStream(cancellation);
        var validator = new ProviderPackageValidator();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => validator.ValidatePackageAsync(
            "example", "1.0.0", "linux", "amd64", filename, shasum, package, shasums, signature,
            "not-reached", cancellation.Token));
    }

    [Fact]
    public async Task ValidatePackageAsyncRejectsSignatureLargerThanConfiguredLimit()
    {
        var packageBytes = Encoding.UTF8.GetBytes("provider package");
        var shasum = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();
        const string filename = "terraform-provider-example_1.0.0_linux_amd64.zip";
        var shasumsText = $"{shasum}  {filename}\n";
        await using var package = new MemoryStream(packageBytes);
        await using var shasums = new MemoryStream(Encoding.UTF8.GetBytes(shasumsText));
        await using var signature = new NonSeekableReadStream([1, 2, 3, 4, 5]);
        var validator = new ProviderPackageValidator(maxSignatureBytes: 4);

        var result = await validator.ValidatePackageAsync(
            "example", "1.0.0", "linux", "amd64", filename, shasum, package, shasums, signature,
            "not-reached", CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Contains("configured limit", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidatePackageAsyncRejectsTamperedNonSeekableSignature()
    {
        var packageBytes = Encoding.UTF8.GetBytes("provider package");
        var shasum = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();
        const string filename = "terraform-provider-example_1.0.0_linux_amd64.zip";
        var shasumsText = $"{shasum}  {filename}\n";
        await using var package = new MemoryStream(packageBytes);
        await using var shasums = new MemoryStream(Encoding.UTF8.GetBytes(shasumsText));
        await using var signature = new NonSeekableReadStream([1, 2, 3, 4]);
        var validator = new ProviderPackageValidator();

        var result = await validator.ValidatePackageAsync(
            "example", "1.0.0", "linux", "amd64", filename, shasum, package, shasums, signature,
            "not-a-public-key", CancellationToken.None);

        Assert.False(result.Valid);
    }

    private static async Task<bool> CommandSucceedsAsync(string fileName, string arguments, string? workingDirectory)
    {
        var result = await RunCommandAsync(fileName, arguments, workingDirectory, captureOutput: false);
        return result.ExitCode == 0;
    }

    private static async Task<string> CaptureCommandAsync(string fileName, string arguments, string workingDirectory)
    {
        var result = await RunCommandAsync(fileName, arguments, workingDirectory, captureOutput: true);
        Assert.Equal(0, result.ExitCode);
        return result.StandardOutput;
    }

    private static async Task<CommandResult> RunCommandAsync(
        string fileName,
        string arguments,
        string? workingDirectory,
        bool captureOutput)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = captureOutput,
            UseShellExecute = false
        };

        process.Start();
        var standardOutputTask = captureOutput ? process.StandardOutput.ReadToEndAsync() : Task.FromResult(string.Empty);
        var standardErrorTask = captureOutput ? process.StandardError.ReadToEndAsync() : Task.FromResult(string.Empty);
        await process.WaitForExitAsync();

        return new CommandResult(
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask);
    }

    private sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class NonSeekableReadStream(byte[] content) : Stream
    {
        private readonly MemoryStream _inner = new(content);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CancellationAwareReadStream(CancellationTokenSource cancellation) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(0);
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"provider-validator-{Guid.NewGuid():N}");

        public TempDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
