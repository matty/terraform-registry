namespace TerraformRegistry.Startup;

/// <summary>
/// Limits applied to inbound VCS webhook requests.
/// </summary>
public sealed class VcsWebhookOptions
{
    public const string SectionName = "Vcs";

    /// <summary>
    /// Largest GitHub webhook body accepted, in bytes.
    /// </summary>
    /// <remarks>
    /// The endpoint is unauthenticated until the HMAC is verified, so the body has to be
    /// bounded before it is buffered. One MiB comfortably clears a normal GitHub push event
    /// while keeping the pre-authentication buffer finite.
    /// </remarks>
    public long GitHubWebhookMaxBodyBytes { get; set; } = DefaultGitHubWebhookMaxBodyBytes;

    public const long DefaultGitHubWebhookMaxBodyBytes = 1L * 1024L * 1024L;

    public void Validate()
    {
        if (GitHubWebhookMaxBodyBytes <= 0)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(GitHubWebhookMaxBodyBytes)} must be greater than zero.");
        }
    }
}
