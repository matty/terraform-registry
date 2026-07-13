using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Tests.UnitTests.Database;

internal static class ExtractionJobRepositoryContract
{
    public static async Task LeasesClaimsRetriesAndDeadLetters(
        IModulePublicationRepository publications,
        IModuleExtractionJobRepository jobs)
    {
        var now = DateTime.UtcNow;
        var attempt = new ModulePublicationAttempt
        {
            Id = Guid.NewGuid(),
            Namespace = "acme",
            Name = "network",
            Provider = "aws",
            Version = "1.0.0",
            State = ModulePublicationAttemptState.Staged,
            StagingKey = $"staging/{Guid.NewGuid():N}",
            CreatedAt = now,
            UpdatedAt = now
        };
        var job = new ModuleExtractionJob
        {
            Id = Guid.NewGuid(),
            PublicationAttemptId = attempt.Id,
            Namespace = attempt.Namespace,
            Name = attempt.Name,
            Provider = attempt.Provider,
            Version = attempt.Version,
            State = ModuleExtractionJobState.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };

        await publications.CreatePublicationAttemptWithExtractionJobAsync(attempt, job);
        Assert.Equal(1, await jobs.CountPendingExtractionJobsAsync());

        var firstClaim = await jobs.TryClaimNextExtractionJobAsync("worker-a", TimeSpan.FromMilliseconds(1));
        Assert.NotNull(firstClaim);
        Assert.Equal(ModuleExtractionJobState.Processing, firstClaim.State);
        Assert.Equal("worker-a", firstClaim.OwnerId);
        Assert.Equal(1, firstClaim.AttemptCount);
        Assert.False(await jobs.TryHeartbeatExtractionJobAsync(job.Id, "worker-b", TimeSpan.FromMinutes(1)));
        Assert.True(await jobs.TryHeartbeatExtractionJobAsync(job.Id, "worker-a", TimeSpan.FromMilliseconds(1)));

        await Task.Delay(25);
        var reclaimed = await jobs.TryClaimNextExtractionJobAsync("worker-b", TimeSpan.FromMinutes(1));
        Assert.NotNull(reclaimed);
        Assert.Equal("worker-b", reclaimed.OwnerId);
        Assert.Equal(2, reclaimed.AttemptCount);
        Assert.False(await jobs.TryCompleteExtractionJobAsync(job.Id, "worker-a"));
        Assert.True(await jobs.TryFailExtractionJobAsync(job.Id, "worker-b", "transient failure", 3));

        var retry = await jobs.TryClaimNextExtractionJobAsync("worker-c", TimeSpan.FromMinutes(1));
        Assert.NotNull(retry);
        Assert.Equal(3, retry.AttemptCount);
        Assert.True(await jobs.TryFailExtractionJobAsync(job.Id, "worker-c", "terminal failure", 3));

        var deadLetter = await publications.GetExtractionJobAsync(job.Id);
        Assert.NotNull(deadLetter);
        Assert.Equal(ModuleExtractionJobState.DeadLetter, deadLetter.State);
        Assert.Equal("terminal failure", deadLetter.LastError);
        Assert.NotNull(deadLetter.CompletedAt);
        Assert.Equal(0, await jobs.CountPendingExtractionJobsAsync());
    }
}
