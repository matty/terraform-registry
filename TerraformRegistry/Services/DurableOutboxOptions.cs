namespace TerraformRegistry.Services;

public sealed class DurableOutboxOptions
{
    public int WorkerConcurrency { get; set; } = 2;
    public int LeaseSeconds { get; set; } = 60;
    public int RetryLimit { get; set; } = 5;
    public int PollIntervalMilliseconds { get; set; } = 500;

    public void Validate()
    {
        if (WorkerConcurrency <= 0 || LeaseSeconds <= 0 || RetryLimit <= 0 || PollIntervalMilliseconds <= 0)
            throw new InvalidOperationException("Durable outbox worker limits must be greater than zero.");
    }
}
