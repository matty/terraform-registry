using System.Text.Json;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Handlers;

public static class GpgHandlers
{
    public static async Task<IResult> AddGpgKey(
        string @namespace,
        GpgKey key,
        IProviderService providerService,
        HttpContext context)
    {
        // Simple validation
        if (string.IsNullOrEmpty(key.AsciiArmor))
            return Results.BadRequest(new { error = "AsciiArmor is required." });

        if (string.IsNullOrEmpty(key.KeyId))
            return Results.BadRequest(new { error = "KeyId is required." });

        // Ensure namespace matches the URL path
        key.Namespace = @namespace;
        key.CreatedAt = DateTime.UtcNow;

        await providerService.AddGpgKeyAsync(key);
        return Results.Created($"/v1/providers/keys/{@namespace}/{key.KeyId}", key);
    }

    public static async Task<IResult> GetGpgKeys(
        string @namespace,
        IProviderService providerService)
    {
        var keys = await providerService.GetGpgKeysAsync(@namespace);
        return Results.Ok(keys);
    }

    public static async Task<IResult> GetGpgKey(
        string @namespace,
        string keyId,
        IProviderService providerService)
    {
        var key = await providerService.GetGpgKeyAsync(@namespace, keyId);
        if (key == null) return Results.NotFound();
        return Results.Ok(key);
    }
}
