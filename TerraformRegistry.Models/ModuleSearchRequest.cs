using System.Text.Json.Serialization;

namespace TerraformRegistry.Models;

/// <summary>
///     Represents a request to search for modules
/// </summary>
public class ModuleSearchRequest
{
    [JsonPropertyName("q")] public string? Q { get; set; }

    [JsonPropertyName("namespace")] public string? Namespace { get; set; }

    [JsonPropertyName("provider")] public string? Provider { get; set; }

    /// <summary>
    ///     Field to order results by: <c>name</c>, <c>published</c> or <c>versions</c>.
    ///     When unset, results keep the registry protocol's namespace/name/provider order.
    /// </summary>
    [JsonPropertyName("sort")] public string? Sort { get; set; }

    /// <summary>
    ///     Order direction: <c>asc</c> (default) or <c>desc</c>.
    /// </summary>
    [JsonPropertyName("order")] public string? Order { get; set; }

    [JsonPropertyName("offset")] public int Offset { get; set; } = 0;

    [JsonPropertyName("limit")] public int Limit { get; set; } = 10;
}
