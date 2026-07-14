using TerraformRegistry.Models;
using TerraformRegistry.Services;
using TerraformRegistry.Services.Mirror;

namespace TerraformRegistry.Tests.UnitTests;

public sealed class MirrorDownloadAdmissionTests
{
    [Fact]
    public void AdmissionEnforcesGlobalAndPerCoordinateLimitsAndReleasesCapacity()
    {
        using var listener = new OperationalMetricsTestListener();
        using var metrics = new OperationalMetrics();
        var admission = new MirrorDownloadAdmission(metrics);
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
        Assert.Contains(listener.Measurements, measurement =>
            measurement.Name == "terraform_registry.mirror.admission_rejections");
        Assert.Contains(listener.Measurements, measurement =>
            measurement.Name == "terraform_registry.mirror.active_downloads");
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
