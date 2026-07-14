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
