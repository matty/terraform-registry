using Moq;
using System.Diagnostics.Metrics;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services;
using TerraformRegistry.Services.Mirror;

namespace TerraformRegistry.Tests.UnitTests;

public sealed class MirrorCacheBudgetServiceTests
{
    [Fact]
    public async Task EnsuresCapacityByEvictingReadyProviderPackagesOldestFirst()
    {
        var providerRepository = new Mock<IProviderMirrorRepository>();
        providerRepository.Setup(x => x.ListProviderPackagesAsync(null, "ready", 1000, 0))
            .ReturnsAsync(
            [
                ProviderPackage("old", 6, DateTime.UtcNow.AddMinutes(-2)),
                ProviderPackage("new", 4, DateTime.UtcNow.AddMinutes(-1))
            ]);
        providerRepository.Setup(x => x.ListProviderPackagesAsync(null, "ready", 1000, 2))
            .ReturnsAsync([]);
        var moduleRepository = new Mock<IModuleMirrorRepository>();
        moduleRepository.Setup(x => x.ListModulePackagesAsync(null, "ready", 1000, 0)).ReturnsAsync([]);
        var storage = new Mock<IProviderArtifactStorage>();
        storage.Setup(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var modules = new Mock<IModuleService>();
        var service = new MirrorCacheBudgetService(providerRepository.Object, moduleRepository.Object, storage.Object, modules.Object, new MirrorCacheUsage());

        var result = await service.EnsureCapacityAsync(5, 10, CancellationToken.None);

        Assert.True(result);
        storage.Verify(x => x.DeleteAsync("old", It.IsAny<CancellationToken>()), Times.Once);
        storage.Verify(x => x.DeleteAsync("new", It.IsAny<CancellationToken>()), Times.Never);
        providerRepository.Verify(x => x.UpsertProviderPackageAsync(It.Is<MirrorProviderPackage>(p =>
            p.PackageStoragePath == null && p.CacheSizeBytes == 0 && p.State == "evicted")), Times.Once);
    }

    [Fact]
    public async Task EnsuresCapacitySkipsAnActiveProviderPackage()
    {
        var active = ProviderPackage("active", 6, DateTime.UtcNow.AddMinutes(-2));
        var idle = ProviderPackage("idle", 6, DateTime.UtcNow.AddMinutes(-1));
        idle = idle with { Version = "2.0.0" };
        var providerRepository = new Mock<IProviderMirrorRepository>();
        providerRepository.Setup(x => x.ListProviderPackagesAsync(null, "ready", 1000, 0))
            .ReturnsAsync([active, idle]);
        providerRepository.Setup(x => x.ListProviderPackagesAsync(null, "ready", 1000, 2)).ReturnsAsync([]);
        var moduleRepository = new Mock<IModuleMirrorRepository>();
        moduleRepository.Setup(x => x.ListModulePackagesAsync(null, "ready", 1000, 0)).ReturnsAsync([]);
        var storage = new Mock<IProviderArtifactStorage>();
        storage.Setup(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var modules = new Mock<IModuleService>();
        var usage = new MirrorCacheUsage();
        using var lease = usage.Acquire("provider:registry.example.com:hashicorp:aws:1.0.0:linux:amd64");
        var service = new MirrorCacheBudgetService(providerRepository.Object, moduleRepository.Object, storage.Object, modules.Object, usage);

        var result = await service.EnsureCapacityAsync(5, 12, CancellationToken.None);

        Assert.True(result);
        storage.Verify(x => x.DeleteAsync("active", It.IsAny<CancellationToken>()), Times.Never);
        storage.Verify(x => x.DeleteAsync("idle", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PurgeProviderRefusesAnActivePackage()
    {
        var package = ProviderPackage("active", 6, DateTime.UtcNow);
        var providers = new Mock<IProviderMirrorRepository>();
        providers.Setup(x => x.GetProviderPackageAsync(
                package.Hostname, package.Namespace, package.Type, package.Version, package.Os, package.Arch))
            .ReturnsAsync(package);
        var storage = new Mock<IProviderArtifactStorage>();
        var usage = new MirrorCacheUsage();
        using var lease = usage.Acquire("provider:registry.example.com:hashicorp:aws:1.0.0:linux:amd64");
        var service = new MirrorCacheBudgetService(providers.Object, Mock.Of<IModuleMirrorRepository>(), storage.Object,
            Mock.Of<IModuleService>(), usage);

        var result = await service.PurgeProviderAsync(package.Hostname, package.Namespace, package.Type, package.Version,
            package.Os, package.Arch, CancellationToken.None);

        Assert.Equal(MirrorCachePurgeResult.InUse, result);
        storage.Verify(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PurgeProviderRefusesAPackageWithAnActiveDistributedLease()
    {
        var package = ProviderPackage("active", 6, DateTime.UtcNow);
        var providers = new Mock<IProviderMirrorRepository>();
        providers.Setup(x => x.GetProviderPackageAsync(
                package.Hostname, package.Namespace, package.Type, package.Version, package.Os, package.Arch))
            .ReturnsAsync(package);
        var leases = new Mock<IMirrorLeaseRepository>();
        leases.Setup(x => x.GetLeaseAsync(
                "provider-package:registry.example.com:hashicorp:aws:1.0.0:linux:amd64", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MirrorCacheLease
            {
                LeaseKey = "provider-package:registry.example.com:hashicorp:aws:1.0.0:linux:amd64",
                OperationType = "provider-package",
                OwnerInstanceId = "other-instance",
                ExpiresAt = DateTime.UtcNow.AddMinutes(1)
            });
        var storage = new Mock<IProviderArtifactStorage>();
        var service = new MirrorCacheBudgetService(providers.Object, Mock.Of<IModuleMirrorRepository>(), storage.Object,
            Mock.Of<IModuleService>(), new MirrorCacheUsage(), leases.Object);

        var result = await service.PurgeProviderAsync(package.Hostname, package.Namespace, package.Type, package.Version,
            package.Os, package.Arch, CancellationToken.None);

        Assert.Equal(MirrorCachePurgeResult.InUse, result);
        storage.Verify(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PurgeModuleRefusesAnActivePackage()
    {
        var package = new MirrorModulePackage
        {
            Hostname = "registry.example.com",
            Namespace = "terraform-aws-modules",
            Name = "vpc",
            Provider = "aws",
            Version = "1.0.0",
            DownloadUrl = "https://registry.example.com/package",
            PackageStoragePath = "cache/vpc"
        };
        var modules = new Mock<IModuleMirrorRepository>();
        modules.Setup(x => x.GetModulePackageAsync(package.Hostname, package.Namespace, package.Name, package.Provider,
                package.Version))
            .ReturnsAsync(package);
        var usage = new MirrorCacheUsage();
        using var lease = usage.Acquire("module:registry.example.com:terraform-aws-modules:vpc:aws:1.0.0");
        var service = new MirrorCacheBudgetService(Mock.Of<IProviderMirrorRepository>(), modules.Object,
            Mock.Of<IProviderArtifactStorage>(), Mock.Of<IModuleService>(), usage);

        var result = await service.PurgeModuleAsync(package.Hostname, package.Namespace, package.Name, package.Provider,
            package.Version, CancellationToken.None);

        Assert.Equal(MirrorCachePurgeResult.InUse, result);
    }

    [Fact]
    public async Task PurgeProviderRefreshesCacheBytesAfterRemovingThePackage()
    {
        using var listener = new MeterListener();
        var cacheBytes = new List<long>();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Name == "terraform_registry.mirror.cache_bytes")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == "terraform_registry.mirror.cache_bytes")
                cacheBytes.Add(value);
        });
        listener.Start();

        var package = ProviderPackage("cache/aws.zip", 42, DateTime.UtcNow);
        var providers = new Mock<IProviderMirrorRepository>();
        providers.Setup(x => x.GetProviderPackageAsync(
                package.Hostname, package.Namespace, package.Type, package.Version, package.Os, package.Arch))
            .ReturnsAsync(package);
        providers.Setup(x => x.ListProviderPackagesAsync(null, "ready", 1000, 0)).ReturnsAsync([]);
        var modules = new Mock<IModuleMirrorRepository>();
        modules.Setup(x => x.ListModulePackagesAsync(null, "ready", 1000, 0)).ReturnsAsync([]);
        var storage = new Mock<IProviderArtifactStorage>();
        storage.Setup(x => x.DeleteAsync(package.PackageStoragePath!, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        using var metrics = new OperationalMetrics();
        var service = new MirrorCacheBudgetService(providers.Object, modules.Object, storage.Object,
            Mock.Of<IModuleService>(), new MirrorCacheUsage(), metrics: metrics);

        Assert.Equal(MirrorCachePurgeResult.Purged, await service.PurgeProviderAsync(
            package.Hostname, package.Namespace, package.Type, package.Version, package.Os, package.Arch,
            CancellationToken.None));
        listener.RecordObservableInstruments();

        Assert.Contains(0, cacheBytes);
    }

    [Fact]
    public async Task RecordCacheBytesRecordsZeroForEmptyRepositoryPages()
    {
        using var listener = new MeterListener();
        var cacheBytes = new List<long>();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Name == "terraform_registry.mirror.cache_bytes")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == "terraform_registry.mirror.cache_bytes")
                cacheBytes.Add(value);
        });
        listener.Start();

        var providers = new Mock<IProviderMirrorRepository>();
        providers.Setup(x => x.ListProviderPackagesAsync(null, "ready", 1000, 0))
            .ReturnsAsync([]);
        var modules = new Mock<IModuleMirrorRepository>();
        modules.Setup(x => x.ListModulePackagesAsync(null, "ready", 1000, 0))
            .ReturnsAsync([]);
        using var metrics = new OperationalMetrics();
        var service = new MirrorCacheBudgetService(providers.Object, modules.Object, Mock.Of<IProviderArtifactStorage>(),
            Mock.Of<IModuleService>(), new MirrorCacheUsage(), metrics: metrics);

        await service.RecordCacheBytesAsync(CancellationToken.None);
        listener.RecordObservableInstruments();

        providers.Verify(x => x.ListProviderPackagesAsync(null, "ready", 1000, 0), Times.Once);
        modules.Verify(x => x.ListModulePackagesAsync(null, "ready", 1000, 0), Times.Once);
        Assert.Contains(0, cacheBytes);
    }

    [Fact]
    public async Task RecordCacheBytesRejectsNullRepositoryPages()
    {
        var providers = new Mock<IProviderMirrorRepository>();
        providers.Setup(x => x.ListProviderPackagesAsync(null, "ready", 1000, 0))
            .ReturnsAsync((IReadOnlyList<MirrorProviderPackage>)null!);
        var service = new MirrorCacheBudgetService(providers.Object, Mock.Of<IModuleMirrorRepository>(),
            Mock.Of<IProviderArtifactStorage>(), Mock.Of<IModuleService>(), new MirrorCacheUsage());

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.RecordCacheBytesAsync(CancellationToken.None));
    }

    private static MirrorProviderPackage ProviderPackage(string path, long size, DateTime updatedAt) => new()
    {
        Hostname = "registry.example.com",
        Namespace = "hashicorp",
        Type = "aws",
        Version = "1.0.0",
        Os = "linux",
        Arch = "amd64",
        DownloadUrl = "https://registry.example.com/package",
        PackageStoragePath = path,
        SizeBytes = size,
        CacheSizeBytes = size,
        State = "ready",
        LastSyncAt = updatedAt,
        UpdatedAt = updatedAt
    };
}
