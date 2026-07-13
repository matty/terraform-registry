using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TerraformRegistry.Startup;

namespace TerraformRegistry.Tests.UnitTests;

public sealed class RateLimitOptionsTests
{
    [Fact]
    public void DefaultsDeclareEveryPlannedIngressPolicy()
    {
        var options = new RegistryRateLimitOptions();

        Assert.Equivalent(
            new[]
            {
                RateLimitPolicyNames.ModuleUpload,
                RateLimitPolicyNames.ProviderUpload,
                RateLimitPolicyNames.WebhookIngress,
                RateLimitPolicyNames.ApiKeyVerification,
                RateLimitPolicyNames.MirrorIngress
            },
            options.Policies.Keys);

        Assert.All(options.Policies.Values, static policy => policy.Validate());
    }

    [Fact]
    public void PartitionKeyUsesAuthenticatedPrincipalBeforeRemoteAddress()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.9");
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "user-123")], "test"));

        Assert.Equal("principal:user-123", RateLimitPartitionKey.For(context));
    }

    [Fact]
    public void PartitionKeyUsesRemoteAddressForAnonymousRequests()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.9");

        Assert.Equal("ip:203.0.113.9", RateLimitPartitionKey.For(context));
    }

    [Theory]
    [InlineData("principal:user-123", "principal")]
    [InlineData("ip:203.0.113.9", "ip")]
    [InlineData("ip:unknown", "ip")]
    public void PartitionCategoryExcludesTheRawPartitionValue(string key, string expected)
    {
        Assert.Equal(expected, RateLimitPartitionKey.CategoryFor(key));
    }

    [Fact]
    public void InvalidPolicyConfigurationFailsValidation()
    {
        var policy = new RegistryRateLimitPolicyOptions { PermitLimit = 0 };

        var exception = Assert.Throws<InvalidOperationException>(policy.Validate);

        Assert.Contains("PermitLimit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PolicyLimiterEnforcesConfiguredConcurrencyLimit()
    {
        var policy = new RegistryRateLimitPolicyOptions
        {
            PermitLimit = 2,
            WindowSeconds = 60,
            ConcurrencyLimit = 1
        };
        using var limiter = RegistryRateLimiterFactory.Create(policy);
        using var first = limiter.AttemptAcquire();
        using var second = limiter.AttemptAcquire();

        Assert.True(first.IsAcquired);
        Assert.False(second.IsAcquired);
    }

    [Fact]
    public void ServiceRegistrationValidatesAndExposesNamedRateLimitFoundation()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection();

        services.AddRegistryRateLimiting(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<RegistryRateLimitOptions>();

        Assert.Contains(RateLimitPolicyNames.ModuleUpload, options.Policies.Keys);
        Assert.NotNull(provider.GetRequiredService<RegistryRateLimitMetrics>());
    }
}
