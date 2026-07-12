using Microsoft.Extensions.Configuration;
using TerraformRegistry.Services;

namespace TerraformRegistry.Tests.UnitTests;

public sealed class ArtifactDownloadTokenServiceTests
{
    [Fact]
    public void TokenCreatedByOneInstanceIsValidatedByAnotherInstanceForTheSamePurpose()
    {
        var issuer = CreateService();
        var validator = CreateService();

        var token = issuer.Create("module", "acme/example-aws-1.0.0.zip", TimeSpan.FromMinutes(10));

        Assert.True(validator.TryValidate(token, "module", out var path));
        Assert.Equal("acme/example-aws-1.0.0.zip", path);
    }

    [Fact]
    public void TokenIsUrlSafeAndCannotBeValidatedForAnotherPurpose()
    {
        var service = CreateService();
        var token = service.Create("provider", "acme/example/1.0.0/linux_amd64.zip", TimeSpan.FromMinutes(10));

        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
        Assert.False(service.TryValidate(token, "module", out _));
    }

    [Fact]
    public void ExpiredOrTamperedTokenIsRejected()
    {
        var service = CreateService();
        var expired = service.Create("module", "module.zip", TimeSpan.FromSeconds(-1));
        var valid = service.Create("module", "module.zip", TimeSpan.FromMinutes(10));
        var tampered = valid[..^1] + (valid[^1] == 'A' ? 'B' : 'A');

        Assert.False(service.TryValidate(expired, "module", out _));
        Assert.False(service.TryValidate(tampered, "module", out _));
    }

    [Fact]
    public void ShortSigningKeyIsRejected()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ArtifactDownloadTokens:SigningKey"] = "short"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() => new ArtifactDownloadTokenService(configuration));

        Assert.Contains("at least 32 characters", exception.Message, StringComparison.Ordinal);
    }

    private static ArtifactDownloadTokenService CreateService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ArtifactDownloadTokens:SigningKey"] = "test-signing-key-that-is-long-enough-to-be-safe-0123456789"
            })
            .Build();
        return new ArtifactDownloadTokenService(configuration);
    }
}
