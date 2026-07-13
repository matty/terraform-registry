using System.Security.Cryptography;
using System.Text;
using System.Diagnostics.Metrics;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services;
using TerraformRegistry.Startup;

namespace TerraformRegistry.Tests.UnitTests;

public sealed class ApiKeyServiceSecurityTests
{
    [Fact]
    public async Task CreateApiKeyAsyncStoresVersionedDigestInsteadOfArgon2Hash()
    {
        var database = new Mock<IDatabaseService>();
        database.Setup(x => x.AddApiKeyAsync(It.IsAny<ApiKey>())).Returns(Task.CompletedTask);
        var service = CreateService(database);

        var (_, key) = await service.CreateApiKeyAsync("user-1", "test");

        Assert.StartsWith("v1:", key.TokenHash, StringComparison.Ordinal);
        database.Verify(x => x.AddApiKeyAsync(key), Times.Once);
    }

    [Fact]
    public async Task ValidateApiKeyAsyncUpgradesLegacyArgon2HashAfterSuccessfulVerification()
    {
        const string token = "12345678legacy-api-key-token";
        var key = new ApiKey
        {
            Id = Guid.NewGuid(),
            UserId = "user-1",
            Prefix = token[..8],
            TokenHash = CreateLegacyHash(token)
        };
        var database = new Mock<IDatabaseService>();
        database.Setup(x => x.GetApiKeysByPrefixAsync(key.Prefix)).ReturnsAsync([key]);
        database.Setup(x => x.GetUserByIdAsync(key.UserId)).ReturnsAsync(new User { Id = key.UserId, IsActive = true });
        database.Setup(x => x.UpdateApiKeyAsync(key)).Returns(Task.CompletedTask);
        var service = CreateService(database);

        var result = await service.ValidateApiKeyAsync(token);

        Assert.Same(key, result.Key);
        Assert.StartsWith("v1:", key.TokenHash, StringComparison.Ordinal);
        database.Verify(x => x.UpdateApiKeyAsync(key), Times.Once);
    }

    [Fact]
    public async Task ValidateApiKeyAsyncCoalescesLastUsedWritesWithinConfiguredInterval()
    {
        var database = new Mock<IDatabaseService>();
        database.Setup(x => x.AddApiKeyAsync(It.IsAny<ApiKey>())).Returns(Task.CompletedTask);
        var service = CreateService(database);
        var (token, key) = await service.CreateApiKeyAsync("user-1", "test");
        database.Setup(x => x.GetApiKeysByPrefixAsync(key.Prefix)).ReturnsAsync([key]);
        database.Setup(x => x.GetUserByIdAsync(key.UserId)).ReturnsAsync(new User { Id = key.UserId, IsActive = true });
        database.Setup(x => x.UpdateApiKeyAsync(key)).Returns(Task.CompletedTask);

        await service.ValidateApiKeyAsync(token);
        await service.ValidateApiKeyAsync(token);

        database.Verify(x => x.UpdateApiKeyAsync(key), Times.Once);
    }

    [Fact]
    public void VerificationGateRejectsRequestsBeyondPerPrefixRateLimit()
    {
        var options = new ApiKeySecurityOptions
        {
            DigestKey = "test-api-key-digest-key-at-least-thirty-two-characters",
            VerificationPermitLimit = 1,
            MaxConcurrentVerificationsPerPartition = 1
        };
        var measurements = new List<(string Policy, string Partition)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "TerraformRegistry.RateLimiting" &&
                instrument.Name == "terraform_registry.rate_limit.rejections")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            string? policy = null;
            string? partition = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "policy") policy = tag.Value as string;
                if (tag.Key == "partition") partition = tag.Value as string;
            }

            if (policy is not null && partition is not null)
            {
                measurements.Add((policy, partition));
            }
        });
        listener.Start();
        using var metrics = new RegistryRateLimitMetrics();
        using var gate = new ApiKeyVerificationGate(options, metrics);

        using var first = gate.TryEnterPrefix("prefix-1");
        var second = gate.TryEnterPrefix("prefix-1");

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Contains((RateLimitPolicyNames.ApiKeyVerification, "prefix"), measurements);
    }

    [Fact]
    public async Task ValidateApiKeyAsyncMarksVerificationPermitExhaustionAsRateLimited()
    {
        var security = new ApiKeySecurityOptions
        {
            DigestKey = "test-api-key-digest-key-at-least-thirty-two-characters",
            VerificationPermitLimit = 1,
            VerificationWindowSeconds = 60
        };
        var database = new Mock<IDatabaseService>();
        database.Setup(x => x.AddApiKeyAsync(It.IsAny<ApiKey>())).Returns(Task.CompletedTask);
        var service = new ApiKeyService(database.Object, Mock.Of<ILogger<ApiKeyService>>(), new UserAdmissionOptions(),
            security, new ApiKeyVerificationGate(security, new RegistryRateLimitMetrics()));
        var (token, key) = await service.CreateApiKeyAsync("user-1", "test");
        database.Setup(x => x.GetApiKeysByPrefixAsync(key.Prefix)).ReturnsAsync([key]);
        database.Setup(x => x.GetUserByIdAsync(key.UserId)).ReturnsAsync(new User { Id = key.UserId, IsActive = true });
        database.Setup(x => x.UpdateApiKeyAsync(key)).Returns(Task.CompletedTask);

        var first = await service.ValidateApiKeyAsync(token);
        var second = await service.ValidateApiKeyAsync(token);

        Assert.False(first.IsRateLimited);
        Assert.True(second.IsRateLimited);
    }

    private static ApiKeyService CreateService(Mock<IDatabaseService> database)
    {
        var security = new ApiKeySecurityOptions { DigestKey = "test-api-key-digest-key-at-least-thirty-two-characters" };
        return new ApiKeyService(database.Object, Mock.Of<ILogger<ApiKeyService>>(), new UserAdmissionOptions(), security,
            new ApiKeyVerificationGate(security, new RegistryRateLimitMetrics()));
    }

    private static string CreateLegacyHash(string token)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(token))
        {
            Salt = salt,
            DegreeOfParallelism = 2,
            MemorySize = 65536,
            Iterations = 4
        };
        return $"{Convert.ToBase64String(salt)}${Convert.ToBase64String(argon2.GetBytes(32))}";
    }
}
