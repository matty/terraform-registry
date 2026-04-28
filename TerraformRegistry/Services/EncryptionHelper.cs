using System.Security.Cryptography;

namespace TerraformRegistry.Services;

public static class EncryptionHelper
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public static string Encrypt(string plainText, string keyBase64)
    {
        var key = Convert.FromBase64String(keyBase64);
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plainBytes, ciphertext, tag);

        return $"{Convert.ToBase64String(nonce)}:{Convert.ToBase64String(ciphertext)}:{Convert.ToBase64String(tag)}";
    }

    public static string Decrypt(string encrypted, string keyBase64)
    {
        var key = Convert.FromBase64String(keyBase64);
        var parts = encrypted.Split(':');
        if (parts.Length != 3)
        {
            throw new FormatException("Invalid encrypted format. Expected nonce:ciphertext:tag");
        }

        var nonce = Convert.FromBase64String(parts[0]);
        var ciphertext = Convert.FromBase64String(parts[1]);
        var tag = Convert.FromBase64String(parts[2]);
        var plainBytes = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plainBytes);

        return System.Text.Encoding.UTF8.GetString(plainBytes);
    }
}
