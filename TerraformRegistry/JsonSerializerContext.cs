namespace TerraformRegistry;

using System.Text.Json.Serialization;
using Models;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ServiceDiscovery))]
[JsonSerializable(typeof(Module))]
[JsonSerializable(typeof(ModuleList))]
[JsonSerializable(typeof(ModuleListItem))]
[JsonSerializable(typeof(ModuleMetadata))]
[JsonSerializable(typeof(ModuleSearchRequest))]
[JsonSerializable(typeof(ModuleStorage))]
[JsonSerializable(typeof(ModuleSubmodule))]
[JsonSerializable(typeof(ModuleVersions))]
[JsonSerializable(typeof(ModuleMetadata))]
public partial class AppJsonSerializerContext : JsonSerializerContext
{
}
