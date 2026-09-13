namespace TerraformRegistry.Startup;

/// <summary>
/// Turns an application-level upload limit into the transport limit for its endpoint.
/// </summary>
/// <remarks>
/// Kestrel defaults <c>MaxRequestBodySize</c> to 30,000,000 bytes. The registry advertises
/// much larger upload limits (100 MiB module archives, 512 MiB provider packages), so without
/// a per-endpoint limit the server rejects anything above the default with a bare 413 before
/// the handler can apply, or report, the configured limit. Raising the limit per endpoint
/// keeps every other route on the conservative server default.
/// </remarks>
public static class UploadRequestLimits
{
    /// <summary>
    /// Headroom added on top of the application limit for multipart framing: the boundary
    /// markers, per-part headers, and trailing CRLFs that surround the payload. Without it a
    /// file of exactly the configured size would be refused by the transport, and the caller
    /// would get an opaque 413 instead of the handler's Terraform-shaped error.
    /// </summary>
    public const long MultipartOverheadBytes = 1L * 1024L * 1024L;

    /// <summary>
    /// Returns the request body limit for an endpoint accepting an upload of
    /// <paramref name="applicationLimitBytes"/>.
    /// </summary>
    public static long ForUploadOf(long applicationLimitBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(applicationLimitBytes);

        // Saturate rather than overflow if someone configures a limit near long.MaxValue.
        return applicationLimitBytes > long.MaxValue - MultipartOverheadBytes
            ? long.MaxValue
            : applicationLimitBytes + MultipartOverheadBytes;
    }
}
