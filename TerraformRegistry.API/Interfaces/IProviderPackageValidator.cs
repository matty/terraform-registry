namespace TerraformRegistry.API.Interfaces;

public interface IProviderPackageValidator
{
    Task<ProviderPackageValidationResult> ValidatePackageAsync(
        string providerType,
        string version,
        string os,
        string arch,
        string filename,
        string expectedShasum,
        Stream package,
        Stream shasums,
        Stream shasumsSignature,
        string asciiArmorPublicKey,
        CancellationToken cancellationToken);
}

public sealed record ProviderPackageValidationResult(bool Valid, string? Error);
