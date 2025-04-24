namespace TerraformRegistry.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Represents a service discovery response for Terraform Registry
/// </summary>
public class ServiceDiscovery
{
    [JsonPropertyName("modules")]
    public Dictionary<string, string> Modules { get; set; } = new()
    {
        { "service-discovery", "/.well-known/terraform.json" },
        { "modules", "/v1/modules/" }
    };
}