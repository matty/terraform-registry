using System.Text.Json.Serialization;

namespace TerraformRegistry.Models;

/// <summary>
///     Represents a service discovery response for Terraform Registry
/// </summary>
public class ServiceDiscovery
{
    [JsonPropertyName("modules.v1")]
    public string ModulesV1 { get; set; } = "/v1/modules/";

    [JsonPropertyName("providers.v1")]
    public string ProvidersV1 { get; set; } = "/v1/providers/";
}