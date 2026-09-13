namespace TerraformRegistry.Startup;

/// <summary>
/// Decides the <c>Secure</c> attribute for authentication cookies.
/// </summary>
public static class CookieSecurity
{
    /// <summary>
    /// Returns whether authentication cookies must be marked <c>Secure</c>.
    /// </summary>
    /// <remarks>
    /// This is deliberately keyed off the hosting environment rather than
    /// <see cref="HttpRequest.IsHttps"/>. Behind a TLS-terminating proxy the inbound request
    /// arrives as plain HTTP even though the user reached us over HTTPS, so keying off
    /// <c>IsHttps</c> silently drops the attribute and leaves the session cookie able to
    /// travel in the clear. Development is the one exception: a <c>Secure</c> cookie is never
    /// returned over <c>http://localhost</c>, which would break local sign-in.
    /// </remarks>
    public static bool IsSecure(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return !environment.IsDevelopment();
    }
}
