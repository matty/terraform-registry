using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading.Tasks;

namespace TerraformRegistry.Middleware
{
    public class AuthenticationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string _authToken;
        private readonly ILogger<AuthenticationMiddleware> _logger;
        private const string AuthorizationHeader = "Authorization";
        private const string BearerPrefix = "Bearer ";
        private static readonly string[] ProtectedPathPrefixes = new[] { "/v1/" };

        public AuthenticationMiddleware(RequestDelegate next, string authToken, ILogger<AuthenticationMiddleware> logger)
        {
            _next = next;
            _authToken = authToken;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value ?? string.Empty;
            if (ProtectedPathPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                var header = context.Request.Headers[AuthorizationHeader].FirstOrDefault();
                if (string.IsNullOrEmpty(header) || !header.Equals($"{BearerPrefix}{_authToken}", StringComparison.Ordinal))
                {
                    _logger.LogWarning("Unauthorized request to {Path} from {RemoteIp}", path, context.Connection.RemoteIpAddress);
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.Headers["WWW-Authenticate"] = "Bearer";
                    await context.Response.WriteAsync("Unauthorized: missing or invalid Authorization token.");
                    return;
                }
            }
            await _next(context);
        }
    }
}
