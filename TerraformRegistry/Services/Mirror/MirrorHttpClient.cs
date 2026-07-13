using System.Net;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services.Mirror;

public sealed class MirrorHttpClient(
    IHttpClientFactory httpClientFactory,
    IMirrorPolicyService policyService,
    ILogger<MirrorHttpClient> logger)
{
    private const string ClientName = "TerraformRegistryMirror";
    private const int BufferSize = 81920;

    public Task<MirrorFetchResult> FetchModuleArchiveAsync(
        string archiveUrl,
        long maxBytes,
        int maxRedirects,
        CancellationToken cancellationToken = default) =>
        FetchModuleArchiveAsync(archiveUrl, maxBytes, maxRedirects, 120, cancellationToken);

    public async Task<MirrorFetchResult> FetchModuleArchiveAsync(
        string archiveUrl,
        long maxBytes,
        int maxRedirects,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        if (maxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes), "Maximum byte count must be positive.");

        if (maxRedirects < 0)
            throw new ArgumentOutOfRangeException(nameof(maxRedirects), "Maximum redirects must not be negative.");
        if (timeoutSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "Timeout must be positive.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var requestToken = timeout.Token;
        var currentEndpoint = await policyService.ValidateModuleArchiveUrlAsync(archiveUrl, requestToken);
        var httpClient = httpClientFactory.CreateClient(ClientName);

        for (var redirectCount = 0; ; redirectCount++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, currentEndpoint.Uri);
            MirrorPinnedConnectionHelper.AttachValidatedAddresses(request, currentEndpoint.Addresses);
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                requestToken);

            if (IsRedirect(response.StatusCode))
            {
                if (redirectCount >= maxRedirects)
                    throw new InvalidOperationException("Mirror fetch exceeded the maximum redirect count.");

                if (response.Headers.Location is null)
                    throw new InvalidOperationException("Mirror fetch redirect did not include a Location header.");

                var redirectUri = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(currentEndpoint.Uri, response.Headers.Location);

                currentEndpoint = await policyService.ValidateModuleArchiveUrlAsync(redirectUri.ToString(), requestToken);
                continue;
            }

            response.EnsureSuccessStatusCode();

            var content = await ReadContentAsync(response, currentEndpoint.Uri, maxBytes, requestToken);
            return new MirrorFetchResult
            {
                Content = content,
                ContentType = response.Content.Headers.ContentType?.MediaType,
                ContentLength = response.Content.Headers.ContentLength,
                StatusCode = response.StatusCode,
                FinalUri = currentEndpoint.Uri
            };
        }
    }

    private async Task<Stream> ReadContentAsync(
        HttpResponseMessage response,
        Uri currentUri,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > 0 and var contentLength && contentLength > maxBytes)
            throw new InvalidOperationException("Mirror fetch response exceeded the maximum byte count.");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        var destination = new MemoryStream(response.Content.Headers.ContentLength is > 0 and var length && length <= int.MaxValue
            ? (int)length
            : 0);
        var buffer = new byte[BufferSize];
        long totalBytes = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
                break;

            totalBytes += read;
            if (totalBytes > maxBytes)
            {
                await destination.DisposeAsync();
                RegistryLog.Warning(logger, "Mirror fetch for {Uri} exceeded maximum byte count {MaxBytes}", currentUri, maxBytes);
                throw new InvalidOperationException("Mirror fetch response exceeded the maximum byte count.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        destination.Position = 0;
        return destination;
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Moved
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
}
