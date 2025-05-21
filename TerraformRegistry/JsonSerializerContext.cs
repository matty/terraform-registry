using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using TerraformRegistry.Models;

namespace TerraformRegistry;

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
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(UploadModuleResponse))]
[JsonSerializable(typeof(StringArrayWrapper))]
[JsonSerializable(typeof(string[]))]
public partial class AppJsonSerializerContext : JsonSerializerContext
{
}