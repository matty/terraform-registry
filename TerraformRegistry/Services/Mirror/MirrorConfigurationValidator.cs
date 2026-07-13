using TerraformRegistry.Models;

namespace TerraformRegistry.Services.Mirror;

public static class MirrorConfigurationValidator
{
    public static void Validate(MirrorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        ValidateHttpsUri(options.UpstreamRegistryBaseUrl, "Mirror:UpstreamRegistryBaseUrl");

        var hosts = options.Providers.AllowedHostnames
            .Where(static host => !string.IsNullOrWhiteSpace(host))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (options.Providers.UpstreamRegistryUrls.Count == 0)
        {
            var upstreamHost = new Uri(options.UpstreamRegistryBaseUrl, UriKind.Absolute).DnsSafeHost;
            if (!hosts.Contains(upstreamHost, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Each allowed provider hostname requires an explicit upstream mapping when it does not exactly match the legacy upstream host.");
            }
        }
        else
        {
            foreach (var host in hosts)
            {
                if (!TryGetMapping(options.Providers.UpstreamRegistryUrls, host, out var upstream))
                {
                    throw new InvalidOperationException($"Provider hostname '{host}' requires an explicit upstream mapping.");
                }

                ValidateHttpsUri(upstream, $"Mirror:Providers:UpstreamRegistryUrls:{host}");
            }

            if (options.Providers.UpstreamRegistryUrls.Keys.Any(key => !hosts.Contains(key, StringComparer.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Provider upstream mappings must correspond to an allowed provider hostname.");
            }
        }

        if (options.Limits.MaxConcurrentDownloads <= 0 ||
            options.Limits.MaxConcurrentDownloadsPerCoordinate <= 0 ||
            options.Limits.MaxConcurrentDownloadsPerCoordinate > options.Limits.MaxConcurrentDownloads ||
            options.Limits.MaxTotalCachedBytes <= 0 ||
            options.Limits.NegativeCacheTtlSeconds <= 0 ||
            options.Providers.DownloadTimeoutSeconds <= 0 ||
            options.Modules.DownloadTimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("Mirror runtime limits must be positive and per-coordinate concurrency cannot exceed global concurrency.");
        }
    }

    public static Uri GetProviderUpstreamUri(MirrorOptions options, string hostname)
    {
        Validate(options);
        if (TryGetMapping(options.Providers.UpstreamRegistryUrls, hostname, out var upstream))
        {
            return ToBaseUri(upstream);
        }

        return ToBaseUri(options.UpstreamRegistryBaseUrl);
    }

    private static bool TryGetMapping(IReadOnlyDictionary<string, string> mappings, string hostname, out string upstream)
    {
        foreach (var mapping in mappings)
        {
            if (string.Equals(mapping.Key, hostname, StringComparison.OrdinalIgnoreCase))
            {
                upstream = mapping.Value;
                return true;
            }
        }

        upstream = string.Empty;
        return false;
    }

    private static void ValidateHttpsUri(string value, string settingName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            string.IsNullOrWhiteSpace(uri.DnsSafeHost))
        {
            throw new InvalidOperationException($"{settingName} must be an absolute HTTPS URI without userinfo.");
        }
    }

    private static Uri ToBaseUri(string value) => new(value.TrimEnd('/') + "/", UriKind.Absolute);
}
