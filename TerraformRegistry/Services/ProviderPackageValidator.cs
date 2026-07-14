using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Bcpg.OpenPgp;
using TerraformRegistry.API.Interfaces;

namespace TerraformRegistry.Services;

public sealed class ProviderPackageValidator : IProviderPackageValidator
{
    public const long DefaultMaxSignatureBytes = 5_242_880;
    private readonly long _maxSignatureBytes;

    public ProviderPackageValidator(long maxSignatureBytes = DefaultMaxSignatureBytes)
    {
        if (maxSignatureBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxSignatureBytes));

        _maxSignatureBytes = maxSignatureBytes;
    }

    public async Task<ProviderPackageValidationResult> ValidatePackageAsync(
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
        CancellationToken cancellationToken)
    {
        var shasumsText = await new StreamReader(shasums, leaveOpen: true).ReadToEndAsync(cancellationToken);
        var parsed = ParseShasums(shasumsText);
        var metadataResult = ValidatePackageMetadata(providerType, version, os, arch, filename, expectedShasum, parsed);
        if (!metadataResult.Valid) return metadataResult;

        var actualShasum = await ComputeSha256HexAsync(package, cancellationToken);
        if (!string.Equals(actualShasum, expectedShasum, StringComparison.OrdinalIgnoreCase))
            return new ProviderPackageValidationResult(false, "Provider package SHA256 does not match platform shasum.");

        var signatureResult = await VerifyDetachedSignatureAsync(shasumsText, shasumsSignature, asciiArmorPublicKey, cancellationToken);
        if (!signatureResult.Valid)
            return new ProviderPackageValidationResult(false, signatureResult.Error ?? "SHA256SUMS signature could not be verified with the selected GPG key.");

        return new ProviderPackageValidationResult(true, null);
    }

    public static string ExpectedProviderPackageFilename(string providerType, string version, string os, string arch) =>
        $"terraform-provider-{providerType}_{version}_{os}_{arch}.zip";

    public static async Task<string> ComputeSha256HexAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream.CanSeek) stream.Position = 0;
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        if (stream.CanSeek) stream.Position = 0;
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static Dictionary<string, string> ParseShasums(string shasums)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in shasums.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 2)
                result[parts[^1]] = parts[0];
        }

        return result;
    }

    public static ProviderPackageValidationResult ValidatePackageMetadata(
        string providerType,
        string version,
        string os,
        string arch,
        string filename,
        string expectedShasum,
        IReadOnlyDictionary<string, string> shasums)
    {
        var expectedFilename = ExpectedProviderPackageFilename(providerType, version, os, arch);
        if (!string.Equals(filename, expectedFilename, StringComparison.Ordinal))
            return new ProviderPackageValidationResult(false, $"Provider package filename must be {expectedFilename}.");

        if (!shasums.TryGetValue(filename, out var listedShasum))
            return new ProviderPackageValidationResult(false, "SHA256SUMS does not contain the provider package filename.");

        if (!string.Equals(listedShasum, expectedShasum, StringComparison.OrdinalIgnoreCase))
            return new ProviderPackageValidationResult(false, "SHA256SUMS entry does not match platform shasum.");

        return new ProviderPackageValidationResult(true, null);
    }

    private async Task<SignatureVerificationResult> VerifyDetachedSignatureAsync(
        string shasumsText,
        Stream signatureStream,
        string asciiArmorPublicKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var bufferedSignature = new MemoryStream();
            var buffer = new byte[81_920];
            long copied = 0;
            while (true)
            {
                var read = await signatureStream.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;

                copied = checked(copied + read);
                if (copied > _maxSignatureBytes)
                {
                    return new SignatureVerificationResult(false,
                        $"SHA256SUMS signature exceeds the configured limit of {_maxSignatureBytes} bytes.");
                }

                await bufferedSignature.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            bufferedSignature.Position = 0;

            using var keyInput = new MemoryStream(Encoding.UTF8.GetBytes(asciiArmorPublicKey));
            using var decodedKeyInput = PgpUtilities.GetDecoderStream(keyInput);
            var publicKeys = new PgpPublicKeyRingBundle(decodedKeyInput);

            using var decodedSignatureInput = PgpUtilities.GetDecoderStream(bufferedSignature);
            var signatureFactory = new PgpObjectFactory(decodedSignatureInput);
            var signatureObject = signatureFactory.NextPgpObject();
            if (signatureObject is PgpCompressedData compressedData)
            {
                signatureFactory = new PgpObjectFactory(compressedData.GetDataStream());
                signatureObject = signatureFactory.NextPgpObject();
            }

            var signatureList = signatureObject as PgpSignatureList;
            if (signatureList == null || signatureList.Count == 0) return new SignatureVerificationResult(false, null);

            var signature = signatureList[0];
            var publicKey = publicKeys.GetPublicKey(signature.KeyId);
            if (publicKey == null) return new SignatureVerificationResult(false, null);

            signature.InitVerify(publicKey);
            var shasumsBytes = Encoding.UTF8.GetBytes(shasumsText);
            signature.Update(shasumsBytes, 0, shasumsBytes.Length);

            return new SignatureVerificationResult(signature.Verify(), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new SignatureVerificationResult(false, null);
        }
    }

    private sealed record SignatureVerificationResult(bool Valid, string? Error);
}
