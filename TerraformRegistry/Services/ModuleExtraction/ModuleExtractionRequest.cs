namespace TerraformRegistry.Services.ModuleExtraction;

public sealed record ModuleExtractionRequest(string Namespace, string Name, string Provider, string Version);
