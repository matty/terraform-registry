using System.Text.Json.Serialization;

namespace TerraformRegistry.Models;

/// <summary>
///     Represents a service discovery response for Terraform Registry
/// </summary>
public class ServiceDiscovery
{
    [JsonPropertyName("modules.v1")]
    public string ModulesV1 { get; set; } = "/v1/modules/";

    [JsonPropertyName("login.v1")]
    public TerraformLoginDiscovery LoginV1 { get; set; } = new();

    // [JsonPropertyName("providers.v1")]
    // public string ProvidersV1 { get; set; } = "/v1/providers/";
}

public class TerraformLoginDiscovery
{
    [JsonPropertyName("client")]
    public string Client { get; set; } = "terraform-cli";

    [JsonPropertyName("grant_types")]
    public string[] GrantTypes { get; set; } = ["authz_code"];

    [JsonPropertyName("authz")]
    public string Authz { get; set; } = "/api/auth/terraform/authorize";

    [JsonPropertyName("token")]
    public string Token { get; set; } = "/api/auth/terraform/token";
}
