using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Services;

namespace TerraformRegistry.Tests.UnitTests;

public class StorageInitializationHostedServiceTests
{
    [Fact]
    public async Task StartAsyncInitializesStorageAndMarksReadinessOnlyAfterItCompletes()
    {
        var moduleService = new Mock<IModuleService>();
        var initializationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowInitializationToComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        moduleService
            .Setup(service => service.InitializeStorageAsync(It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                initializationStarted.SetResult();
                await allowInitializationToComplete.Task;
            });

        var services = new ServiceCollection();
        services.AddSingleton(moduleService.Object);
        var readiness = new StartupReadiness();
        var hostedService = new StorageInitializationHostedService(
            services.BuildServiceProvider(),
            readiness,
            NullLogger<StorageInitializationHostedService>.Instance);

        var startTask = hostedService.StartAsync(CancellationToken.None);
        await initializationStarted.Task;
        Assert.False(readiness.IsStorageInitialized);

        allowInitializationToComplete.SetResult();
        await startTask;

        Assert.True(readiness.IsStorageInitialized);
        moduleService.Verify(service => service.InitializeStorageAsync(CancellationToken.None), Times.Once);
    }
}
