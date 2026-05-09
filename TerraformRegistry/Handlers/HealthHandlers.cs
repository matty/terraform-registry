using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Services;

namespace TerraformRegistry.Handlers;

/// <summary>
///     Handlers for health and readiness endpoints
/// </summary>
public static class HealthHandlers
{
    public static IResult HandleHealth()
    {
        return Results.Ok(new { status = "healthy" });
    }

    public static async Task<IResult> HandleReady(
        IDatabaseService dbService,
        IModuleService moduleService,
        IProviderArtifactStorage providerArtifactStorage,
        HttpContext context,
        IConfiguration configuration)
    {
        var dbTask = dbService.CheckConnectionAsync();
        var storageTask = moduleService.CheckStorageAsync();
        var providerStorageTask = providerArtifactStorage.CheckStorageAsync(context.RequestAborted);
        await Task.WhenAll(dbTask, storageTask, providerStorageTask);

        var dbHealthy = await dbTask;
        var (storageHealthy, storageReason) = await storageTask;
        var (providerStorageHealthy, providerStorageReason) = await providerStorageTask;
        var isReady = dbHealthy && storageHealthy && providerStorageHealthy;

        var wantDetail = string.Equals(
            context.Request.Query["detail"].FirstOrDefault(),
            "true",
            StringComparison.OrdinalIgnoreCase);

        var showDetail = wantDetail && await IsAuthenticatedAsync(context, configuration);

        if (showDetail)
        {
            var response = new
            {
                status = isReady ? "ready" : "not_ready",
                checks = new
                {
                    database = new
                    {
                        status = dbHealthy ? "healthy" : "unhealthy"
                    },
                    storage = new
                    {
                        status = storageHealthy ? "healthy" : "unhealthy",
                        reason = storageReason
                    },
                    providerArtifactStorage = new
                    {
                        status = providerStorageHealthy ? "healthy" : "unhealthy",
                        reason = providerStorageReason
                    }
                }
            };

            return isReady ? Results.Ok(response) : Results.Json(response, statusCode: 503);
        }

        var minimal = new { status = isReady ? "ready" : "not_ready" };
        return isReady ? Results.Ok(minimal) : Results.Json(minimal, statusCode: 503);
    }

    private static async Task<bool> IsAuthenticatedAsync(HttpContext context, IConfiguration configuration)
    {
        // Check 1: Static bearer token
        var header = context.Request.Headers["Authorization"].FirstOrDefault();
        if (!string.IsNullOrEmpty(header) && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = header["Bearer ".Length..];
            var staticToken = configuration["AuthorizationToken"];
            if (!string.IsNullOrEmpty(staticToken) && string.Equals(token, staticToken, StringComparison.Ordinal))
            {
                return true;
            }

            // Check 2: API key
            if (!token.Contains('.') || token.Count(c => c == '.') != 2)
            {
                using var scope = context.RequestServices.CreateScope();
                var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
                var result = await apiKeyService.ValidateApiKeyAsync(token);
                if (result.Key != null && !result.IsExpired)
                {
                    return true;
                }
            }
        }

        // Check 3: Session cookie
        const string sessionCookieName = "tf-session";
        var sessionToken = context.Request.Cookies[sessionCookieName];
        if (!string.IsNullOrEmpty(sessionToken))
        {
            var jwtService = context.RequestServices.GetRequiredService<JwtService>();
            var principal = jwtService.ValidateToken(sessionToken);
            if (principal != null)
            {
                return true;
            }
        }

        return false;
    }
}
