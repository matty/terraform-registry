using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;
using TerraformRegistry.API.Utilities;
using TerraformRegistry.Models;
using TerraformRegistry.Services.Publishing;

namespace TerraformRegistry.Services;

public class GitHubVcsService : IGitHubVcsService
{
    private readonly IVcsSourceService _vcsSourceService;
    private readonly IVcsConnectionService _vcsConnectionService;
    private readonly IModuleService? _moduleService;
    private readonly IModulePublishCoordinator _publishCoordinator;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GitHubVcsService> _logger;
    private readonly long _maxArchiveBytes;

    public GitHubVcsService(
        IVcsSourceService vcsSourceService,
        IVcsConnectionService vcsConnectionService,
        IModulePublishCoordinator publishCoordinator,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<GitHubVcsService> logger)
        : this(
            vcsSourceService,
            vcsConnectionService,
            null,
            publishCoordinator,
            httpClientFactory,
            configuration,
            logger)
    {
    }

    public GitHubVcsService(
        IVcsSourceService vcsSourceService,
        IVcsConnectionService vcsConnectionService,
        IModuleService? moduleService,
        IModulePublishCoordinator publishCoordinator,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<GitHubVcsService> logger)
    {
        _vcsSourceService = vcsSourceService;
        _vcsConnectionService = vcsConnectionService;
        _moduleService = moduleService;
        _publishCoordinator = publishCoordinator;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
        _maxArchiveBytes = configuration.GetValue(
            "ModuleExtraction:MaxArchiveBytes",
            ModuleExtractionOptions.DefaultMaxArchiveBytes);
    }

    public async Task<(string Status, string? Reason, string? Version)> HandleWebhookAsync(
        string? signatureHeader, string? eventHeader, string body)
    {
        if (eventHeader != "push")
        {
            return ("skipped", $"Event type '{eventHeader}' is not handled", null);
        }

        JsonElement payload;
        try
        {
            payload = JsonSerializer.Deserialize<JsonElement>(body);
        }
        catch (JsonException ex)
        {
            return ("error", $"Invalid JSON payload: {ex.Message}", null);
        }

        if (!payload.TryGetProperty("ref", out var refElement))
        {
            return ("error", "Missing 'ref' in payload", null);
        }

        var gitRef = refElement.GetString() ?? string.Empty;
        if (!gitRef.StartsWith("refs/tags/", StringComparison.Ordinal))
        {
            return ("skipped", "Push is not a tag", null);
        }

        if (!payload.TryGetProperty("repository", out var repo))
        {
            return ("error", "Missing 'repository' in payload", null);
        }

        var repoOwner = repo.GetProperty("owner").GetProperty("login").GetString() ?? string.Empty;
        var repoName = repo.GetProperty("name").GetString() ?? string.Empty;

        var vcsSource = await _vcsSourceService.GetByRepoAsync(repoOwner, repoName);
        if (vcsSource == null)
        {
            return ("skipped", $"No active VCS source for {repoOwner}/{repoName}", null);
        }

        // Look up the VCS connection for webhook secret and PAT
        var vcsConnection = await _vcsConnectionService.GetConnectionAsync(vcsSource.ConnectionId);
        if (vcsConnection == null)
        {
            return ("error", $"VCS connection {vcsSource.ConnectionId} not found for source {vcsSource.Id}", null);
        }

        // Verify HMAC-SHA256 signature
        if (string.IsNullOrEmpty(signatureHeader) || !signatureHeader.StartsWith("sha256=", StringComparison.Ordinal))
        {
            return ("error", "Missing or invalid signature header", null);
        }

        var expectedSignature = signatureHeader["sha256=".Length..];
        var keyBytes = Encoding.UTF8.GetBytes(vcsConnection.WebhookSecret);
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var computedHash = HMACSHA256.HashData(keyBytes, bodyBytes);
        var computedSignature = Convert.ToHexStringLower(computedHash);

        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedSignature),
            Encoding.UTF8.GetBytes(expectedSignature)))
        {
            return ("error", "Signature verification failed", null);
        }

        if (!vcsConnection.IsActive)
        {
            return ("skipped", $"Inactive VCS connection {vcsConnection.Id}", null);
        }

        // Parse version from tag
        var tag = gitRef["refs/tags/".Length..];
        var version = tag.StartsWith('v') ? tag[1..] : tag;

        if (!SemVerValidator.IsValid(version))
        {
            return ("skipped", $"Tag '{tag}' is not a valid SemVer version", null);
        }

        // Download tarball from GitHub
        string? pat = null;
        if (!string.IsNullOrEmpty(vcsConnection.PatEncrypted))
        {
            try
            {
                var encryptionKey = _configuration["EncryptionKey"] ?? throw new InvalidOperationException("EncryptionKey not configured");
                pat = EncryptionHelper.Decrypt(vcsConnection.PatEncrypted, encryptionKey);
            }
            catch (Exception ex)
            {
                RegistryLog.Error(_logger, ex, "Failed to decrypt PAT for VCS connection {ConnectionId}, deactivating connection", vcsConnection.Id);
                await _vcsConnectionService.UpdateConnectionAsync(vcsConnection.Id, null, null, null, false);
                return ("error", "Failed to decrypt PAT; VCS connection has been deactivated", null);
            }
        }

        var client = _httpClientFactory.CreateClient("GitHubVcs");
        var tarballUrl = $"https://api.github.com/repos/{repoOwner}/{repoName}/tarball/{tag}";

        using var request = new HttpRequestMessage(HttpMethod.Get, tarballUrl);
        request.Headers.Add("User-Agent", "TerraformRegistry");
        request.Headers.Add("Accept", "application/vnd.github+json");
        if (pat != null)
        {
            request.Headers.Add("Authorization", $"Bearer {pat}");
        }

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        }
        catch (Exception ex)
        {
            RegistryLog.Error(_logger, ex, "Failed to download tarball from {Url}", tarballUrl);
            return ("error", $"Failed to download tarball: {ex.Message}", null);
        }

        if (!response.IsSuccessStatusCode)
        {
            return ("error", $"GitHub API returned {(int)response.StatusCode} for tarball download", null);
        }

        await using var tarballStream = await response.Content.ReadAsStreamAsync();
        var uploaded = await _publishCoordinator.PublishAsync(new ModulePublishRequest
        {
            Namespace = vcsSource.Namespace,
            Name = vcsSource.Name,
            Provider = vcsSource.Provider,
            Version = version,
            Description = $"Auto-published from {repoOwner}/{repoName} tag {tag}",
            ModuleContent = tarballStream,
            Replace = false,
            ActorUserId = null,
            AuditAction = "vcs.auto_published",
            Metadata = new ModuleArtifactMetadata
            {
                Source = new ModuleSourceInfo
                {
                    Kind = "vcs-tag",
                    RepoOwner = repoOwner,
                    RepoName = repoName,
                    RepoUrl = $"https://github.com/{repoOwner}/{repoName}",
                    Ref = $"refs/tags/{tag}"
                }
            }
        }, CancellationToken.None);

        if (!uploaded)
        {
            await _vcsSourceService.UpdateSyncStateAsync(vcsSource.Id, "failed", vcsSource.LastPublishedVersion, $"Module upload failed for version {version}");
            return ("error", $"Module upload failed for version {version}", null);
        }

        await _vcsSourceService.UpdateSyncStateAsync(vcsSource.Id, "succeeded", version, null);
        return ("published", null, version);
    }

    public async Task<SyncVcsSourceResult> SyncSourceAsync(
        Guid sourceId,
        string? requestedTag,
        bool replace,
        string? actorUserId,
        CancellationToken cancellationToken)
    {
        var source = await _vcsSourceService.GetAsync(sourceId)
            ?? throw new InvalidOperationException($"VCS source {sourceId} was not found.");

        if (!source.IsActive)
            throw new InvalidOperationException($"VCS source {sourceId} is inactive.");

        var connection = await _vcsConnectionService.GetConnectionAsync(source.ConnectionId)
            ?? throw new InvalidOperationException($"VCS connection {source.ConnectionId} was not found.");

        if (!connection.IsActive)
            throw new InvalidOperationException($"VCS connection {source.ConnectionId} is inactive.");

        try
        {
            var knowsExistingVersions = _moduleService != null;
            var knownVersions = knowsExistingVersions
                ? await GetKnownVersionsAsync(source.Namespace, source.Name, source.Provider)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var published = new List<string>();
            var skipped = 0;

            foreach (var tag in await ListCandidateTagsAsync(source, connection, requestedTag, cancellationToken))
            {
                var version = NormalizeTag(tag);
                if (!replace && knownVersions.Contains(version))
                {
                    skipped++;
                    continue;
                }

                await using var tarballStream = await DownloadTarballAsync(
                    source.RepoOwner,
                    source.RepoName,
                    tag,
                    connection,
                    cancellationToken);

                var uploaded = await _publishCoordinator.PublishAsync(new ModulePublishRequest
                {
                    Namespace = source.Namespace,
                    Name = source.Name,
                    Provider = source.Provider,
                    Version = version,
                    Description = $"Synced from {source.RepoOwner}/{source.RepoName} tag {tag}",
                    ModuleContent = tarballStream,
                    Replace = replace,
                    ActorUserId = actorUserId,
                    AuditAction = "vcs.sync_published",
                    Metadata = new ModuleArtifactMetadata
                    {
                        Source = new ModuleSourceInfo
                        {
                            Kind = "vcs-tag",
                            RepoOwner = source.RepoOwner,
                            RepoName = source.RepoName,
                            RepoUrl = $"https://github.com/{source.RepoOwner}/{source.RepoName}",
                            Ref = $"refs/tags/{tag}"
                        }
                    }
                }, cancellationToken);

                if (uploaded)
                {
                    published.Add(version);
                    knownVersions.Add(version);
                    continue;
                }

                if (!knowsExistingVersions && !replace)
                {
                    skipped++;
                    continue;
                }

                throw new InvalidOperationException($"Module upload failed for version {version}");
            }

            var latestVersion = published.LastOrDefault() ?? source.LastPublishedVersion;
            await _vcsSourceService.UpdateSyncStateAsync(source.Id, "succeeded", latestVersion, null);

            return new SyncVcsSourceResult("succeeded", published.Count, skipped, latestVersion, null);
        }
        catch (Exception ex)
        {
            await _vcsSourceService.UpdateSyncStateAsync(source.Id, "failed", source.LastPublishedVersion, ex.Message);
            RegistryLog.Error(_logger, ex, "VCS sync failed for source {SourceId}", sourceId);
            throw;
        }
    }

    private async Task<HashSet<string>> GetKnownVersionsAsync(string @namespace, string name, string provider)
    {
        if (_moduleService == null)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var existingVersions = await _moduleService.GetModuleVersionsAsync(@namespace, name, provider);
        return existingVersions.Modules
            .SelectMany(module => module.Versions)
            .Select(version => version.Version)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<List<string>> ListCandidateTagsAsync(
        VcsSource source,
        VcsConnection connection,
        string? requestedTag,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedTag))
        {
            if (!SemVerValidator.IsValid(NormalizeTag(requestedTag)))
            {
                throw new InvalidOperationException($"Requested tag '{requestedTag}' is not a valid semantic version tag.");
            }

            return [requestedTag];
        }

        var client = _httpClientFactory.CreateClient("GitHubVcs");
        var discoveredTags = new List<string>();

        for (var page = 1; ; page++)
        {
            using var request = CreateGitHubRequest(
                HttpMethod.Get,
                $"https://api.github.com/repos/{source.RepoOwner}/{source.RepoName}/tags?per_page=100&page={page}",
                connection);

            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var batch = payload.RootElement
                .EnumerateArray()
                .Select(tag => tag.GetProperty("name").GetString() ?? string.Empty)
                .ToList();

            if (batch.Count == 0)
            {
                break;
            }

            discoveredTags.AddRange(batch);

            if (batch.Count < 100)
            {
                break;
            }
        }

        return discoveredTags
            .Where(tag => MatchesTagPattern(source.TagPattern, tag))
            .Where(tag => SemVerValidator.IsValid(NormalizeTag(tag)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => NormalizeTag(tag), SemVerVersionComparer.Instance)
            .ToList();
    }

    private async Task<Stream> DownloadTarballAsync(
        string repoOwner,
        string repoName,
        string tag,
        VcsConnection connection,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("GitHubVcs");
        using var request = CreateGitHubRequest(
            HttpMethod.Get,
            $"https://api.github.com/repos/{repoOwner}/{repoName}/tarball/{tag}",
            connection);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);

        var memory = new MemoryStream();
        try
        {
            await CopyToOwnedStreamAsync(responseStream, memory, _maxArchiveBytes, cancellationToken);
            memory.Position = 0;
            return memory;
        }
        catch
        {
            await memory.DisposeAsync();
            throw;
        }
    }

    private static async Task CopyToOwnedStreamAsync(
        Stream source,
        Stream destination,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (maxBytes <= 0)
        {
            throw new InvalidOperationException("Module archive size limit must be greater than zero bytes.");
        }

        var buffer = new byte[81920];
        long total = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return;
            }

            total += read;
            if (total > maxBytes)
            {
                throw new InvalidOperationException(
                    $"Module archive exceeds the configured limit of {maxBytes} bytes.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private HttpRequestMessage CreateGitHubRequest(HttpMethod method, string url, VcsConnection connection)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("User-Agent", "TerraformRegistry");
        request.Headers.Add("Accept", "application/vnd.github+json");

        var pat = TryDecryptPat(connection);
        if (!string.IsNullOrWhiteSpace(pat))
        {
            request.Headers.Add("Authorization", $"Bearer {pat}");
        }

        return request;
    }

    private string? TryDecryptPat(VcsConnection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.PatEncrypted))
        {
            return null;
        }

        var encryptionKey = _configuration["EncryptionKey"]
            ?? throw new InvalidOperationException("EncryptionKey not configured");
        return EncryptionHelper.Decrypt(connection.PatEncrypted, encryptionKey);
    }

    private static bool MatchesTagPattern(string tagPattern, string tag)
    {
        var prefix = tagPattern.TrimEnd('*');
        return tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTag(string tag) =>
        tag.StartsWith('v') ? tag[1..] : tag;
}
