using System.Security.Claims;

namespace TerraformRegistry.Startup;

/// <summary>
/// Names reserved for expensive ingress policies. Endpoint attachment is deliberately
/// kept in the package which owns each ingress surface.
/// </summary>
public static class RateLimitPolicyNames
{
    public const string ModuleUpload = "module-upload";
    public const string ProviderUpload = "provider-upload";
    public const string WebhookIngress = "webhook-ingress";
    public const string ApiKeyVerification = "api-key-verification";
    public const string MirrorIngress = "mirror-ingress";
}

public sealed class RegistryRateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public Dictionary<string, RegistryRateLimitPolicyOptions> Policies { get; init; } = new(StringComparer.Ordinal)
    {
        [RateLimitPolicyNames.ModuleUpload] = new(),
        [RateLimitPolicyNames.ProviderUpload] = new(),
        [RateLimitPolicyNames.WebhookIngress] = new(),
        [RateLimitPolicyNames.ApiKeyVerification] = new(),
        [RateLimitPolicyNames.MirrorIngress] = new()
    };

    public void Validate()
    {
        foreach (var policyName in new[]
                 {
                     RateLimitPolicyNames.ModuleUpload,
                     RateLimitPolicyNames.ProviderUpload,
                     RateLimitPolicyNames.WebhookIngress,
                     RateLimitPolicyNames.ApiKeyVerification,
                     RateLimitPolicyNames.MirrorIngress
                 })
        {
            if (!Policies.TryGetValue(policyName, out var policy))
            {
                throw new InvalidOperationException($"RateLimiting policy '{policyName}' is required.");
            }

            policy.Validate();
        }
    }
}

public sealed class RegistryRateLimitPolicyOptions
{
    public int PermitLimit { get; init; } = 60;
    public int WindowSeconds { get; init; } = 60;
    public int QueueLimit { get; init; }
    public int ConcurrencyLimit { get; init; } = 4;

    public void Validate()
    {
        if (PermitLimit <= 0)
        {
            throw new InvalidOperationException("RateLimiting PermitLimit must be greater than zero.");
        }

        if (WindowSeconds <= 0)
        {
            throw new InvalidOperationException("RateLimiting WindowSeconds must be greater than zero.");
        }

        if (QueueLimit < 0)
        {
            throw new InvalidOperationException("RateLimiting QueueLimit cannot be negative.");
        }

        if (ConcurrencyLimit <= 0)
        {
            throw new InvalidOperationException("RateLimiting ConcurrencyLimit must be greater than zero.");
        }
    }
}

public static class RateLimitPartitionKey
{
    public static string For(HttpContext context)
    {
        var principalId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(principalId))
        {
            return $"principal:{principalId}";
        }

        return context.Connection.RemoteIpAddress is { } address
            ? $"ip:{address}"
            : "ip:unknown";
    }

    public static string CategoryFor(string partitionKey) =>
        partitionKey.StartsWith("principal:", StringComparison.Ordinal) ? "principal" : "ip";
}
