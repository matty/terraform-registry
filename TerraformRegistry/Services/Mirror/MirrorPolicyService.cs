using System.Net;
using System.Net.Sockets;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;
using TerraformRegistry.API.Utilities;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services.Mirror;

public sealed class MirrorPolicyService(
    IMirrorConfigService mirrorConfigService,
    IWebhookHostResolver hostResolver,
    ILogger<MirrorPolicyService> logger) : IMirrorPolicyService
{
    private static readonly StringComparer HostComparer = StringComparer.OrdinalIgnoreCase;

    public async Task<bool> IsProviderAllowedAsync(
        string hostname,
        string providerNamespace,
        string type,
        string os,
        string arch,
        CancellationToken cancellationToken = default)
    {
        var providerOptions = (await mirrorConfigService.GetConfigAsync(cancellationToken)).Effective.Providers;
        if (!providerOptions.Enabled)
        {
            return false;
        }

        if (!IsValidHostname(hostname) || !ProviderIdentifierValidator.IsValidProviderSegment(providerNamespace)
                                      || !ProviderIdentifierValidator.IsValidProviderSegment(type))
        {
            return false;
        }

        if (providerOptions.AllowedHostnames.Count > 0
            && !providerOptions.AllowedHostnames.Any(x => HostComparer.Equals(x, hostname)))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(os)
            && !string.IsNullOrWhiteSpace(arch)
            && providerOptions.Platforms.Count > 0
            && !providerOptions.Platforms.Contains($"{os}_{arch}", StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var coordinate = new[] { hostname, providerNamespace, type };
        return IsAllowedBySegmentRules(coordinate, providerOptions.Allowlist, providerOptions.Denylist);
    }

    public async Task<bool> IsModuleAllowedAsync(
        string hostname,
        string moduleNamespace,
        string name,
        string provider,
        CancellationToken cancellationToken = default)
    {
        var moduleOptions = (await mirrorConfigService.GetConfigAsync(cancellationToken)).Effective.Modules;
        if (!moduleOptions.Enabled)
        {
            return false;
        }

        if (!IsValidHostname(hostname) || ModuleIdentifierValidator.GetModuleCoordinateError(moduleNamespace, name, provider) is not null)
        {
            return false;
        }

        if (moduleOptions.AllowedNamespaces.Count > 0
            && !moduleOptions.AllowedNamespaces.Contains(moduleNamespace, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var coordinate = new[] { moduleNamespace, name, provider };
        return IsAllowedBySegmentRules(coordinate, moduleOptions.Allowlist, moduleOptions.Denylist);
    }

    public async Task<ValidatedMirrorEndpoint> ValidateModuleArchiveUrlAsync(
        string archiveUrl,
        CancellationToken cancellationToken = default)
    {
        var moduleOptions = (await mirrorConfigService.GetConfigAsync(cancellationToken)).Effective.Modules;
        if (!Uri.TryCreate(archiveUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Mirror module archive URL must be an absolute URI.");
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Mirror module archive URL must use HTTPS.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException("Mirror module archive URL must not include userinfo credentials.");
        }

        var allowedHosts = moduleOptions.AllowedArchiveHosts;
        if (allowedHosts.Count == 0 || !allowedHosts.Any(host => HostComparer.Equals(host, uri.DnsSafeHost)))
        {
            throw new InvalidOperationException("Mirror module archive URL host is not allowed.");
        }

        var addresses = await ResolveAddressesAsync(uri, cancellationToken);
        if (addresses.Length == 0)
        {
            throw new InvalidOperationException("Mirror module archive URL host could not be resolved.");
        }

        foreach (var address in addresses)
        {
            if (IsPrivateOrLocal(address))
            {
                RegistryLog.Warning(logger, "Blocked mirror archive target {Url} because it resolved to {Address}", archiveUrl, address);
                throw new InvalidOperationException("Mirror module archive URL resolves to a private or local address and is not allowed.");
            }
        }

        return new ValidatedMirrorEndpoint(uri, addresses);
    }

    public async Task<ValidatedMirrorEndpoint> ValidateProviderArtifactUrlAsync(
        string artifactUrl,
        CancellationToken cancellationToken = default)
    {
        var providerOptions = (await mirrorConfigService.GetConfigAsync(cancellationToken)).Effective.Providers;
        if (!Uri.TryCreate(artifactUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Mirror provider artifact URL must be an absolute URI.");
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Mirror provider artifact URL must use HTTPS.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException("Mirror provider artifact URL must not include userinfo credentials.");
        }

        if (providerOptions.AllowedArtifactHosts.Count > 0
            && !providerOptions.AllowedArtifactHosts.Any(host => HostComparer.Equals(host, uri.DnsSafeHost)))
        {
            throw new InvalidOperationException("Mirror provider artifact URL host is not allowed.");
        }

        var addresses = await ResolveAddressesAsync(uri, cancellationToken);
        if (addresses.Length == 0)
        {
            throw new InvalidOperationException("Mirror provider artifact URL host could not be resolved.");
        }

        foreach (var address in addresses)
        {
            if (IsPrivateOrLocal(address))
            {
                RegistryLog.Warning(logger, "Blocked provider mirror artifact target {Url} because it resolved to {Address}", artifactUrl, address);
                throw new InvalidOperationException("Mirror provider artifact URL resolves to a private or local address and is not allowed.");
            }
        }

        return new ValidatedMirrorEndpoint(uri, addresses);
    }

    private async Task<IPAddress[]> ResolveAddressesAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(uri.DnsSafeHost, out var address))
        {
            return [address];
        }

        try
        {
            return await hostResolver.ResolveHostAsync(uri.DnsSafeHost, cancellationToken);
        }
        catch (SocketException ex)
        {
            RegistryLog.Warning(logger, ex, "Mirror archive host resolution failed for {Host}", uri.DnsSafeHost);
            throw new InvalidOperationException("Mirror module archive URL host could not be resolved.", ex);
        }
    }

    private static bool IsAllowedBySegmentRules(
        IReadOnlyList<string> coordinate,
        List<string> allowlist,
        List<string> denylist)
    {
        if (denylist.Any(pattern => SegmentPatternMatches(pattern, coordinate)))
        {
            return false;
        }

        return allowlist.Count == 0 || allowlist.Any(pattern => SegmentPatternMatches(pattern, coordinate));
    }

    private static bool SegmentPatternMatches(string pattern, IReadOnlyList<string> coordinate)
    {
        var patternSegments = pattern.Split('/', StringSplitOptions.TrimEntries);
        if (patternSegments.Length != coordinate.Count || patternSegments.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        for (var i = 0; i < patternSegments.Length; i++)
        {
            if (patternSegments[i] == "*")
                continue;

            if (!string.Equals(patternSegments[i], coordinate[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static bool IsValidHostname(string hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname))
        {
            return false;
        }

        return Uri.CheckHostName(hostname) is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6;
    }

    private static bool IsPrivateOrLocal(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();

            return bytes[0] switch
            {
                0 => true,
                10 => true,
                100 when bytes[1] >= 64 && bytes[1] <= 127 => true,
                127 => true,
                169 when bytes[1] == 254 => true,
                172 when bytes[1] >= 16 && bytes[1] <= 31 => true,
                192 when bytes[1] == 0 && bytes[2] == 0 => true,
                192 when bytes[1] == 0 && bytes[2] == 2 => true,
                192 when bytes[1] == 88 && bytes[2] == 99 => true,
                192 when bytes[1] == 168 => true,
                198 when bytes[1] is 18 or 19 => true,
                198 when bytes[1] == 51 && bytes[2] == 100 => true,
                203 when bytes[1] == 0 && bytes[2] == 113 => true,
                >= 224 => true,
                _ => false
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();

            if (address.Equals(IPAddress.IPv6Loopback) || address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal)
                return true;

            if (bytes[0] == 0xfc || bytes[0] == 0xfd)
                return true;

            if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80)
                return true;

            if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8)
                return true;
        }

        return false;
    }
}
