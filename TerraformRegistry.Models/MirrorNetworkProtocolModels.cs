using System.Text.Json.Serialization;

namespace TerraformRegistry.Models;

public sealed class ProviderMirrorIndexResponse
{
    [JsonPropertyName("versions")]
    public required SortedDictionary<string, object> Versions { get; init; }
}

public sealed class ProviderMirrorVersionResponse
{
    [JsonPropertyName("archives")]
    public required Dictionary<string, ProviderMirrorArchive> Archives { get; init; }
}

public sealed class ProviderMirrorArchive
{
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    [JsonPropertyName("hashes")]
    public required string[] Hashes { get; init; }
}

public sealed record ProviderMirrorPackageDownload(
    Stream Content,
    string Filename,
    string ContentType,
    long? ContentLength);
