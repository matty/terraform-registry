using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Utilities;

namespace TerraformRegistry.Services;

public class GitHubVcsService
{
    private readonly IVcsSourceService _vcsSourceService;
    private readonly IModuleService _moduleService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly WebhookDispatcher _webhookDispatcher;
    private readonly ILogger<GitHubVcsService> _logger;

    public GitHubVcsService(
        IVcsSourceService vcsSourceService,
        IModuleService moduleService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        WebhookDispatcher webhookDispatcher,
        ILogger<GitHubVcsService> logger)
    {
        _vcsSourceService = vcsSourceService;
        _moduleService = moduleService;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _webhookDispatcher = webhookDispatcher;
        _logger = logger;
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

        // Verify HMAC-SHA256 signature
        if (string.IsNullOrEmpty(signatureHeader) || !signatureHeader.StartsWith("sha256=", StringComparison.Ordinal))
        {
            return ("error", "Missing or invalid signature header", null);
        }

        var expectedSignature = signatureHeader["sha256=".Length..];
        var keyBytes = Encoding.UTF8.GetBytes(vcsSource.WebhookSecret);
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var computedHash = HMACSHA256.HashData(keyBytes, bodyBytes);
        var computedSignature = Convert.ToHexStringLower(computedHash);

        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedSignature),
            Encoding.UTF8.GetBytes(expectedSignature)))
        {
            return ("error", "Signature verification failed", null);
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
        if (!string.IsNullOrEmpty(vcsSource.PatEncrypted))
        {
            try
            {
                var encryptionKey = _configuration["EncryptionKey"] ?? throw new InvalidOperationException("EncryptionKey not configured");
                pat = EncryptionHelper.Decrypt(vcsSource.PatEncrypted, encryptionKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt PAT for VCS source {VcsSourceId}, marking inactive", vcsSource.Id);
                await _vcsSourceService.UpdateVcsSourceAsync(vcsSource.Id, vcsSource.UserId, null, null, null, false);
                return ("error", "Failed to decrypt PAT; VCS source has been deactivated", null);
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
            _logger.LogError(ex, "Failed to download tarball from {Url}", tarballUrl);
            return ("error", $"Failed to download tarball: {ex.Message}", null);
        }

        if (!response.IsSuccessStatusCode)
        {
            return ("error", $"GitHub API returned {(int)response.StatusCode} for tarball download", null);
        }

        await using var tarballStream = await response.Content.ReadAsStreamAsync();
        var uploaded = await _moduleService.UploadModuleAsync(
            vcsSource.Namespace,
            vcsSource.Name,
            vcsSource.Provider,
            version,
            tarballStream,
            $"Auto-published from {repoOwner}/{repoName} tag {tag}",
            replace: false);

        if (!uploaded)
        {
            return ("error", $"Module upload failed for version {version}", null);
        }

        _webhookDispatcher.FireEvent(
            "module.published",
            vcsSource.Namespace,
            vcsSource.Name,
            vcsSource.Provider,
            version,
            $"Auto-published from {repoOwner}/{repoName} tag {tag}");

        return ("published", null, version);
    }
}
