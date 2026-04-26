using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services;

namespace TerraformRegistry.Controllers;

[ApiController]
[Route("api/keys")]
[Authorize]
public class ApiKeyController(IApiKeyService apiKeyService, IAuditService auditService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ListKeys()
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var owner = await apiKeyService.GetUserByIdAsync(userId);
        var keys = await apiKeyService.ListApiKeysAsync(userId);
        var response = keys.Select(k => MapApiKey(k, owner));

        return Ok(response);
    }

    [HttpGet("shared")]
    public async Task<IActionResult> ListSharedKeys()
    {
        if (!User.HasPermission(Permissions.ApiKeysShared)) return Forbid();

        var keys = await apiKeyService.ListSharedApiKeysAsync();

        // Preload owners to display ownership information
        var owners = await LoadOwners(keys);
        var response = keys.Select(k => MapApiKey(k, owners.GetValueOrDefault(k.UserId)));

        return Ok(response);
    }

    [HttpPost]
    public async Task<IActionResult> CreateKey([FromBody] CreateApiKeyRequest request)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (request.IsShared && !User.HasPermission(Permissions.ApiKeysShared)) return Forbid();

        var (rawToken, key) = await apiKeyService.CreateApiKeyAsync(userId, request.Description, request.IsShared);

        HttpContext.FireAuditLog(auditService, "api_key.created", "api_key", key.Id.ToString(), new { description = request.Description, isShared = request.IsShared });

        var owner = await apiKeyService.GetUserByIdAsync(userId);

        return Ok(new
        {
            token = rawToken,
            apiKey = MapApiKey(key, owner)
        });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateKey(Guid id, [FromBody] UpdateApiKeyRequest request)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var existing = await apiKeyService.GetApiKeyAsync(id);
        if (existing == null) return NotFound();
        if (existing.UserId != userId) return Forbid();
        if ((existing.IsShared || request.IsShared) && !User.HasPermission(Permissions.ApiKeysShared)) return Forbid();

        var result = await apiKeyService.UpdateApiKeyAsync(id, userId, request.Description, request.IsShared);

        if (result.Status == ApiKeyUpdateStatus.NotFound) return NotFound();
        if (result.Status == ApiKeyUpdateStatus.Forbidden) return Forbid();

        var owner = await apiKeyService.GetUserByIdAsync(userId);
        return Ok(MapApiKey(result.Key!, owner));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> RevokeKey(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var success = await apiKeyService.RevokeApiKeyAsync(id, userId);
        if (!success) return NotFound();

        HttpContext.FireAuditLog(auditService, "api_key.revoked", "api_key", id.ToString());

        return NoContent();
    }

    private string GetUserId()
    {
        // Assuming JWT/Auth puts relevant ID in claims
        // Adjust claim type based on what OIDC/JWT Service provides
        var idClaim = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
                      ?? User.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;

        return idClaim ?? string.Empty;
    }

    private ApiKeyResponse MapApiKey(ApiKey key, User? owner)
    {
        return new ApiKeyResponse
        {
            Id = key.Id,
            Description = key.Description,
            Prefix = key.Prefix,
            IsShared = key.IsShared,
            CreatedAt = key.CreatedAt,
            ExpiresAt = key.ExpiresAt,
            LastUsedAt = key.LastUsedAt,
            OwnerUserId = owner?.Id ?? key.UserId,
            OwnerUsername = owner?.ProviderId,
            OwnerEmail = owner?.Email,
            OwnerDisplay = BuildOwnerDisplay(owner, key.UserId)
        };
    }

    private static string? BuildOwnerDisplay(User? owner, string fallbackUserId)
    {
        if (owner == null) return fallbackUserId;

        var username = owner.ProviderId;
        var email = owner.Email;

        if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(email))
            return $"{username} ({email})";

        if (!string.IsNullOrWhiteSpace(username)) return username;
        if (!string.IsNullOrWhiteSpace(email)) return email;

        return fallbackUserId;
    }

    private async Task<Dictionary<string, User?>> LoadOwners(IEnumerable<ApiKey> keys)
    {
        var owners = new Dictionary<string, User?>();

        foreach (var key in keys)
        {
            if (owners.ContainsKey(key.UserId)) continue;
            owners[key.UserId] = await apiKeyService.GetUserByIdAsync(key.UserId);
        }

        return owners;
    }
}

public class CreateApiKeyRequest
{
    public string Description { get; set; } = string.Empty;
    public bool IsShared { get; set; }
}

public class UpdateApiKeyRequest
{
    public string Description { get; set; } = string.Empty;
    public bool IsShared { get; set; }
}

public class ApiKeyResponse
{
    public Guid Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public bool IsShared { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public string? OwnerUserId { get; set; }
    public string? OwnerUsername { get; set; }
    public string? OwnerEmail { get; set; }
    public string? OwnerDisplay { get; set; }
}
