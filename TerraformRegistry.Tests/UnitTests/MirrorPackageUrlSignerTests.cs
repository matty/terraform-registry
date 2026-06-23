using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using TerraformRegistry.Services.Mirror;

namespace TerraformRegistry.Tests.UnitTests;

public sealed class MirrorPackageUrlSignerTests
{
    [Fact]
    public void ValidateRejectsExpiredSignature()
    {
        var signer = CreateSigner();
        var expires = DateTimeOffset.UtcNow.AddMinutes(-1);
        var url = signer.CreateSignedPackageUrl(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            "5.0.0",
            "linux",
            "amd64",
            "terraform-provider-aws_5.0.0_linux_amd64.zip",
            expires);

        Assert.False(signer.TryValidate(url, DateTimeOffset.UtcNow, out _));
    }

    [Fact]
    public void ValidateRejectsTamperedSignaturePayload()
    {
        var signer = CreateSigner();
        var url = signer.CreateSignedPackageUrl(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            "5.0.0",
            "linux",
            "amd64",
            "terraform-provider-aws_5.0.0_linux_amd64.zip",
            DateTimeOffset.UtcNow.AddMinutes(10));
        var tampered = url.Replace("arch=amd64", "arch=arm64", StringComparison.Ordinal);

        Assert.False(signer.TryValidate(tampered, DateTimeOffset.UtcNow, out _));
    }

    [Fact]
    public void ValidateReturnsSignedPackageClaims()
    {
        var signer = CreateSigner();
        var url = signer.CreateSignedPackageUrl(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            "5.0.0",
            "linux",
            "amd64",
            "terraform-provider-aws_5.0.0_linux_amd64.zip",
            DateTimeOffset.UtcNow.AddMinutes(10));

        Assert.True(signer.TryValidate(url, DateTimeOffset.UtcNow, out var claims));
        Assert.Equal("registry.terraform.io", claims.Hostname);
        Assert.Equal("hashicorp", claims.Namespace);
        Assert.Equal("aws", claims.Type);
        Assert.Equal("5.0.0", claims.Version);
        Assert.Equal("linux", claims.Os);
        Assert.Equal("amd64", claims.Arch);
        Assert.Equal("terraform-provider-aws_5.0.0_linux_amd64.zip", claims.Filename);
    }

    [Fact]
    public void ConstructorRejectsKnownPlaceholderOutsideDevelopmentAndTest()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mirror:PackageUrlSigningKey"] = "your-256-bit-secret-key-here-minimum-32-chars"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            new MirrorPackageUrlSigner(configuration, new TestHostEnvironment { EnvironmentName = "Production" }));
    }

    [Fact]
    public void ConstructorAllowsPlaceholderInTest()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mirror:PackageUrlSigningKey"] = "your-256-bit-secret-key-here-minimum-32-chars"
            })
            .Build();

        var signer = new MirrorPackageUrlSigner(configuration, new TestHostEnvironment { EnvironmentName = "Test" });

        Assert.NotNull(signer);
    }

    private static MirrorPackageUrlSigner CreateSigner()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mirror:PackageUrlSigningKey"] = "test-package-url-signing-key-with-32-chars"
            })
            .Build();

        return new MirrorPackageUrlSigner(configuration, new TestHostEnvironment());
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "TerraformRegistry.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
