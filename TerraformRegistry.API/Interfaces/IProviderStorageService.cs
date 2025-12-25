using System.Security.Cryptography;
using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

/// <summary>
/// Interface for Provider Storage
/// </summary>
public interface IProviderStorageService
{
    // Provider Binaries
    Task<string> UploadProviderAsync(string @namespace, string type, string version, string os, string arch, Stream stream);
    Task<string?> GetProviderDownloadUrlAsync(string @namespace, string type, string version, string os, string arch);

    // Checksums and Signatures (Per Version)
    Task UploadShasumsAsync(string @namespace, string type, string version, Stream stream);
    Task UploadShasumsSigAsync(string @namespace, string type, string version, Stream stream);
    Task<string?> GetShasumsDownloadUrlAsync(string @namespace, string type, string version);
    Task<string?> GetShasumsSigDownloadUrlAsync(string @namespace, string type, string version);

    // File Serving (For Local Storage)
    Task<Stream?> GetFileStreamAsync(string relativePath);
}
