namespace TerraformRegistry.Models;

/// <summary>
///     Configuration options for database connection retry behavior during application startup.
/// </summary>
public class DatabaseRetryOptions
{
    /// <summary>
    ///     The maximum number of retry attempts before giving up.
    ///     Default: 5
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 5;

    /// <summary>
    ///     The initial delay in seconds before the first retry attempt.
    ///     Subsequent retries use exponential backoff.
    ///     Default: 2 seconds
    /// </summary>
    public int InitialDelaySeconds { get; set; } = 2;

    /// <summary>
    ///     The maximum delay in seconds between retry attempts.
    ///     Prevents exponential backoff from growing too large.
    ///     Default: 30 seconds
    /// </summary>
    public int MaxDelaySeconds { get; set; } = 30;
}
