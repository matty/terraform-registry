namespace TerraformRegistry.Models;

public sealed record ProviderPackageDetails(
    Guid ProviderId,
    string[] Protocols,
    string KeyId,
    string ShasumsStoragePath,
    string ShasumsSignatureStoragePath,
    string Os,
    string Arch,
    string Filename,
    string Shasum,
    string PackageStoragePath,
    string AsciiArmor,
    string? TrustSignature,
    string? Source,
    string? SourceUrl);
