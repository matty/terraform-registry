namespace TerraformRegistry.Middleware;

public class AuthenticationMiddleware(
    RequestDelegate next,
    string authToken,
    ILogger<AuthenticationMiddleware> logger)
{
    private const string AuthorizationHeader = "Authorization";
    private const string BearerPrefix = "Bearer ";
    private static readonly string[] ProtectedPathPrefixes = new[] { "/v1/" };

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (ProtectedPathPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            var header = context.Request.Headers[AuthorizationHeader].FirstOrDefault();
            if (string.IsNullOrEmpty(header) || !header.Equals($"{BearerPrefix}{authToken}", StringComparison.Ordinal))
            {
                logger.LogWarning("Unauthorized request to {Path} from {RemoteIp}", path,
                    context.Connection.RemoteIpAddress);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers["WWW-Authenticate"] = "Bearer";
                await context.Response.WriteAsync("Unauthorized: missing or invalid Authorization token.");
                return;
            }
        }

        await next(context);
    }
}