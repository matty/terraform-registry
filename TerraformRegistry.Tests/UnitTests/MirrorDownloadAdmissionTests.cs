using TerraformRegistry.Models;
using TerraformRegistry.Services.Mirror;

namespace TerraformRegistry.Tests.UnitTests;

public sealed class MirrorDownloadAdmissionTests
{
    [Fact]
    public void AdmissionEnforcesGlobalAndPerCoordinateLimitsAndReleasesCapacity()
    {
        var admission = new MirrorDownloadAdmission();
        var limits = new MirrorLimitRuntimeOptions
        {
            MaxConcurrentDownloads = 2,
            MaxConcurrentDownloadsPerCoordinate = 1
        };

        using var first = admission.TryAcquire(limits, "provider:aws");
        using var second = admission.TryAcquire(limits, "provider:azurerm");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Null(admission.TryAcquire(limits, "provider:aws"));
        Assert.Null(admission.TryAcquire(limits, "provider:random"));

        second!.Dispose();

        using var replacement = admission.TryAcquire(limits, "provider:random");
        Assert.NotNull(replacement);
    }

    [Fact]
    public void AdmissionAppliesReducedRuntimeLimitsImmediately()
    {
        var admission = new MirrorDownloadAdmission();
        var relaxed = new MirrorLimitRuntimeOptions
        {
            MaxConcurrentDownloads = 2,
            MaxConcurrentDownloadsPerCoordinate = 2
        };
        var strict = new MirrorLimitRuntimeOptions
        {
            MaxConcurrentDownloads = 1,
            MaxConcurrentDownloadsPerCoordinate = 1
        };

        using var first = admission.TryAcquire(relaxed, "provider:aws");
        Assert.NotNull(first);

        Assert.Null(admission.TryAcquire(strict, "provider:azurerm"));
    }
}
