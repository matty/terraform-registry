using TerraformRegistry.API.Logging;
using Microsoft.Extensions.Logging;

namespace TerraformRegistry.Tests.UnitTests;

public sealed class SensitiveDataRedactorTests
{
    [Theory]
    [InlineData("pat=ghp_abcdefghijklmnopqrstuvwxyz1234567890")]
    [InlineData("webhook_secret=webhook-secret-value")]
    [InlineData("api_key=trf_api_key_value")]
    [InlineData("code=oauth-authorization-code")]
    [InlineData("https://storage.example/module?sig=sas-signature&se=2027-01-01")]
    [InlineData("Authorization: Bearer opaque+/=._~-token")]
    [InlineData("authorization=Bearer opaque+/=._~-token")]
    [InlineData("X-Api-Key: api-key-with+/=characters")]
    [InlineData("webhook-secret=webhook-secret-value")]
    public void RedactRemovesCredentialsAndSignedUrlValues(string value)
    {
        var redacted = SensitiveDataRedactor.Redact(value);

        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz1234567890", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("webhook-secret-value", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("trf_api_key_value", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("oauth-authorization-code", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("sas-signature", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("storage.example", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistryLogRedactsStructuredStringArguments()
    {
        var logger = new CapturingLogger();

        RegistryLog.Warning(logger, "Callback failed for {Url}", "https://storage.example/module?sig=sas-signature");

        Assert.DoesNotContain("sas-signature", logger.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("storage.example", logger.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistryLogRedactsTypedS3SignedUriArguments()
    {
        var logger = new CapturingLogger();
        var signedUri = new Uri("https://bucket.s3.example/module.zip?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential=access-key&X-Amz-Date=20260714T120000Z&X-Amz-Expires=300&X-Amz-SignedHeaders=host&X-Amz-Signature=secret-signature");

        RegistryLog.Warning(logger, "Mirror fetch for {Uri} exceeded maximum byte count {MaxBytes}", signedUri, 1024);

        Assert.DoesNotContain("bucket.s3.example", logger.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-signature", logger.Message, StringComparison.Ordinal);
        Assert.Contains("[REDACTED-SIGNED-URL]", logger.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistryLogRedactsTypedAzureSasUriArguments()
    {
        var logger = new CapturingLogger();
        var signedUri = new Uri("https://account.blob.core.windows.net/modules/example.zip?sv=2025-11-05&se=2026-07-15T12%3A00%3A00Z&sp=r&sig=azure-sas-signature");

        RegistryLog.Warning(logger, "Module download for {Uri} failed", signedUri);

        Assert.DoesNotContain("account.blob.core.windows.net", logger.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("azure-sas-signature", logger.Message, StringComparison.Ordinal);
        Assert.Contains("[REDACTED-SIGNED-URL]", logger.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistryLogRedactsExceptionAndInnerExceptionMessages()
    {
        var logger = new CapturingLogger();
        var inner = new InvalidOperationException("download failed at https://storage.example/module?sig=sas-signature&se=2027-01-01");
        var exception = new HttpRequestException("Authorization: Bearer opaque+/=._~-token", inner);

        RegistryLog.Error(logger, exception, "Module download failed");

        Assert.NotNull(logger.Exception);
        Assert.DoesNotContain("sas-signature", logger.Exception!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("storage.example", logger.Exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("opaque+/=._~-token", logger.Exception.ToString(), StringComparison.Ordinal);
        Assert.NotNull(logger.Exception.InnerException);
        Assert.DoesNotContain("sas-signature", logger.Exception.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistryLogRedactsS3SignedUrisInExceptionChains()
    {
        var logger = new CapturingLogger();
        const string signedUrl = "https://bucket.s3.example/module.zip?X-Amz-Credential=access-key&X-Amz-Security-Token=session-token&X-Amz-Signature=secret-signature";
        var exception = new InvalidOperationException("mirror request failed", new HttpRequestException($"download failed at {signedUrl}"));

        RegistryLog.Error(logger, exception, "Module download failed");

        Assert.NotNull(logger.Exception);
        Assert.DoesNotContain("bucket.s3.example", logger.Exception!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("session-token", logger.Exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret-signature", logger.Exception.ToString(), StringComparison.Ordinal);
    }

    private sealed class CapturingLogger : ILogger
    {
        public string Message { get; private set; } = string.Empty;
        public Exception? Exception { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Message = formatter(state, exception);
            Exception = exception;
        }
    }
}
