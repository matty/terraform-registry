using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics.Metrics;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services;
using TerraformRegistry.Services.ModuleExtraction;

namespace TerraformRegistry.Tests.UnitTests;

public class ModuleExtractionQueueRuntimeTests
{
    [Fact]
    public async Task QueueAsyncReturnsFalseAndDoesNotQueueWhenExtractionDisabled()
    {
        var config = new Mock<IModuleExtractionConfigService>();
        config.Setup(x => x.IsEnabledAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var service = CreateService(config.Object);

        var queued = await service.QueueAsync(new ModuleExtractionRequest("acme", "network", "aws", "1.0.0"),
            CancellationToken.None);

        Assert.False(queued);
    }

    [Fact]
    public async Task ProcessNextAsyncRecordsClaimAttemptAndFailureMetricsForAClaimedJob()
    {
        using var listener = new OperationalMetricsTestListener();
        using var metrics = new OperationalMetrics();
        var config = new Mock<IModuleExtractionConfigService>();
        var job = new ModuleExtractionJob
        {
            Id = Guid.NewGuid(),
            PublicationAttemptId = Guid.NewGuid(),
            Namespace = "acme",
            Name = "network",
            Provider = "aws",
            Version = "1.0.0",
            State = ModuleExtractionJobState.Processing,
            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTime.UtcNow
        };
        var database = new Mock<IDatabaseService>();
        database.Setup(x => x.TryClaimNextExtractionJobAsync("worker-1", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        database.Setup(x => x.TryFailExtractionJobAsync(job.Id, "worker-1", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateService(config.Object, database.Object, metrics: metrics);

        Assert.True(await service.ProcessNextAsync("worker-1", CancellationToken.None));
        Assert.Contains(listener.Measurements, measurement => measurement.Name == "terraform_registry.extraction.claim_latency_ms");
        Assert.Contains(listener.Measurements, measurement => measurement.Name == "terraform_registry.extraction.attempts");
        Assert.Contains(listener.Measurements, measurement =>
            measurement.Name == "terraform_registry.extraction.failures" && measurement.Outcome == "processing_failed");
    }

    [Fact]
    public async Task QueueAsyncMarksModulePendingWhenExtractionEnabled()
    {
        var config = new Mock<IModuleExtractionConfigService>();
        config.Setup(x => x.IsEnabledAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var metadata = new ModuleArtifactMetadata();
        var database = new Mock<IDatabaseService>();
        database.Setup(x => x.UpdateModuleMetadataAsync(
                "acme",
                "network",
                "aws",
                "1.0.0",
                It.IsAny<Action<ModuleArtifactMetadata>>()))
            .Callback<string, string, string, string, Action<ModuleArtifactMetadata>>((_, _, _, _, mutate) => mutate(metadata))
            .Returns(Task.CompletedTask);

        var service = CreateService(config.Object, database.Object);

        var queued = await service.QueueAsync(new ModuleExtractionRequest("acme", "network", "aws", "1.0.0"),
            CancellationToken.None);

        Assert.True(queued);
        Assert.NotNull(metadata.Extraction);
        Assert.Equal("pending", metadata.Extraction.Status);
        Assert.NotNull(metadata.Extraction.LastUpdatedAt);
        Assert.NotNull(metadata.LlmContext);
        Assert.Equal("pending", metadata.LlmContext.Status);
        Assert.NotNull(metadata.LlmContext.LastUpdatedAt);
        database.Verify(x => x.CreatePublicationAttemptWithExtractionJobAsync(
            It.Is<ModulePublicationAttempt>(attempt =>
                attempt.Namespace == "acme" && attempt.State == ModulePublicationAttemptState.Committed),
            It.Is<ModuleExtractionJob>(job =>
                job.Namespace == "acme" && job.State == ModuleExtractionJobState.Pending)), Times.Once);
    }

    [Fact]
    public async Task QueueAsyncRestoresMissingLlmContextStateWhenExtractionEnabled()
    {
        var config = new Mock<IModuleExtractionConfigService>();
        config.Setup(x => x.IsEnabledAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var metadata = new ModuleArtifactMetadata { LlmContext = null! };
        var database = new Mock<IDatabaseService>();
        database.Setup(x => x.UpdateModuleMetadataAsync(
                "acme",
                "network",
                "aws",
                "1.0.0",
                It.IsAny<Action<ModuleArtifactMetadata>>()))
            .Callback<string, string, string, string, Action<ModuleArtifactMetadata>>((_, _, _, _, mutate) => mutate(metadata))
            .Returns(Task.CompletedTask);

        var service = CreateService(config.Object, database.Object);

        var queued = await service.QueueAsync(new ModuleExtractionRequest("acme", "network", "aws", "1.0.0"),
            CancellationToken.None);

        Assert.True(queued);
        Assert.NotNull(metadata.LlmContext);
        Assert.Equal("pending", metadata.LlmContext.Status);
        Assert.NotNull(metadata.LlmContext.LastUpdatedAt);
    }

    [Fact]
    public async Task QueueBackfillAsyncQueuesBoundedModulesWhenEnabled()
    {
        var config = new Mock<IModuleExtractionConfigService>();
        config.Setup(x => x.IsEnabledAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var database = new Mock<IDatabaseService>();
        database.Setup(x => x.ListModulesForExtractionBackfillAsync(2)).ReturnsAsync([
            new ModuleStorage
            {
                Namespace = "acme",
                Name = "one",
                Provider = "aws",
                Version = "1.0.0",
                Description = "",
                FilePath = "one.zip",
                Dependencies = []
            },
            new ModuleStorage
            {
                Namespace = "acme",
                Name = "two",
                Provider = "aws",
                Version = "1.0.0",
                Description = "",
                FilePath = "two.zip",
                Dependencies = []
            }
        ]);

        var service = CreateService(config.Object, database.Object);

        var queued = await service.QueueBackfillAsync(2, CancellationToken.None);

        Assert.Equal(2, queued.Count);
    }

    [Fact]
    public async Task QueueBackfillAsyncMarksQueuedModulesPendingWithoutClearingErrors()
    {
        var config = new Mock<IModuleExtractionConfigService>();
        config.Setup(x => x.IsEnabledAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var metadata = new ModuleArtifactMetadata
        {
            Extraction = new ModuleExtractionState { Status = "failed", Error = "tool missing" }
        };

        var database = new Mock<IDatabaseService>();
        database.Setup(x => x.ListModulesForExtractionBackfillAsync(1)).ReturnsAsync([
            new ModuleStorage
            {
                Namespace = "acme",
                Name = "network",
                Provider = "aws",
                Version = "1.0.0",
                Description = "",
                FilePath = "network.zip",
                Dependencies = [],
                Metadata = metadata
            }
        ]);
        database.Setup(x => x.UpdateModuleMetadataAsync(
                "acme",
                "network",
                "aws",
                "1.0.0",
                It.IsAny<Action<ModuleArtifactMetadata>>()))
            .Callback<string, string, string, string, Action<ModuleArtifactMetadata>>((_, _, _, _, mutate) => mutate(metadata))
            .Returns(Task.CompletedTask);

        var service = CreateService(config.Object, database.Object);

        var queued = await service.QueueBackfillAsync(1, CancellationToken.None);

        Assert.Single(queued);
        Assert.Equal("pending", metadata.Extraction.Status);
        Assert.Equal("tool missing", metadata.Extraction.Error);
    }

    [Fact]
    public async Task QueueAsyncRejectsWorkWhenDurableBacklogIsFull()
    {
        var config = new Mock<IModuleExtractionConfigService>();
        config.Setup(x => x.IsEnabledAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var database = new Mock<IDatabaseService>();
        database.Setup(x => x.CountPendingExtractionJobsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var service = CreateService(config.Object, database.Object, new ModuleExtractionOptions { MaxPendingJobs = 1 });

        var queued = await service.QueueAsync(new ModuleExtractionRequest("acme", "network", "aws", "1.0.0"),
            CancellationToken.None);

        Assert.False(queued);
        database.Verify(x => x.CreatePublicationAttemptWithExtractionJobAsync(
            It.IsAny<ModulePublicationAttempt>(), It.IsAny<ModuleExtractionJob>()), Times.Never);
    }

    [Fact]
    public async Task ProcessNextAsyncRefreshesQueueDepthAfterClaimingAJob()
    {
        using var listener = new MeterListener();
        var queueDepths = new List<long>();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Name == "terraform_registry.extraction.queue_depth")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == "terraform_registry.extraction.queue_depth")
                queueDepths.Add(value);
        });
        listener.Start();

        var config = new Mock<IModuleExtractionConfigService>();
        var database = new Mock<IDatabaseService>();
        database.Setup(x => x.TryClaimNextExtractionJobAsync("worker", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModuleExtractionJob
            {
                Id = Guid.NewGuid(),
                PublicationAttemptId = Guid.NewGuid(),
                Namespace = "acme",
                Name = "network",
                Provider = "aws",
                Version = "1.0.0",
                State = ModuleExtractionJobState.Pending,
                UpdatedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
        database.Setup(x => x.CountPendingExtractionJobsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        database.Setup(x => x.TryFailExtractionJobAsync(It.IsAny<Guid>(), "worker", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var moduleService = new Mock<IModuleService>();
        moduleService.Setup(x => x.OpenModulePackageStreamAsync("acme", "network", "aws", "1.0.0"))
            .ReturnsAsync((Stream?)null);
        using var metrics = new OperationalMetrics();
        var service = new ModuleExtractionService(
            moduleService.Object,
            database.Object,
            Mock.Of<IArchiveWorkspaceFactory>(),
            Mock.Of<ITerraformModuleInspector>(),
            Mock.Of<IModuleLlmContextGenerator>(),
            config.Object,
            NullLogger<ModuleExtractionService>.Instance,
            new ModuleExtractionOptions { JobLeaseSeconds = 1 },
            metrics);

        Assert.True(await service.ProcessNextAsync("worker", CancellationToken.None));
        listener.RecordObservableInstruments();

        Assert.Contains(0, queueDepths);
    }

    private static ModuleExtractionService CreateService(
        IModuleExtractionConfigService config,
        IDatabaseService? database = null,
        ModuleExtractionOptions? options = null,
        OperationalMetrics? metrics = null)
    {
        return new ModuleExtractionService(
            Mock.Of<IModuleService>(),
            database ?? Mock.Of<IDatabaseService>(),
            Mock.Of<IArchiveWorkspaceFactory>(),
            Mock.Of<ITerraformModuleInspector>(),
            Mock.Of<IModuleLlmContextGenerator>(),
            config,
            NullLogger<ModuleExtractionService>.Instance,
            options,
            metrics);
    }
}
