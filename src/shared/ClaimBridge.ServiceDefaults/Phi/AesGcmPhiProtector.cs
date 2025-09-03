using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace ClaimBridge.ServiceDefaults.Phi;

public sealed class AesGcmPhiProtector : IPhiProtector
{
    private const string Prefix = "v1:";
    private readonly byte[] _encKey;
    private readonly byte[] _hashKey;

    public AesGcmPhiProtector(byte[] masterKey)
    {
        if (masterKey.Length != 32) throw new ArgumentException("PHI master key must be 32 bytes", nameof(masterKey));
        _encKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, 32, info: "claim-bridge/phi/enc"u8.ToArray());
        _hashKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, 32, info: "claim-bridge/phi/hash"u8.ToArray());
    }

    public static AesGcmPhiProtector FromConfiguration(IConfiguration config)
    {
        var key = config["Phi:Key"] ?? throw new InvalidOperationException("Phi:Key is not configured");
        return new AesGcmPhiProtector(Convert.FromBase64String(key));
    }

    public string Protect(string plaintext)
    {
        var data = Encoding.UTF8.GetBytes(plaintext);
        var buffer = new byte[12 + data.Length + 16];
        var nonce = buffer.AsSpan(0, 12);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_encKey, 16);
        aes.Encrypt(nonce, data, buffer.AsSpan(12, data.Length), buffer.AsSpan(12 + data.Length, 16));
        return Prefix + Convert.ToBase64String(buffer);
    }

    public string Unprotect(string protectedValue)
    {
        if (!protectedValue.StartsWith(Prefix, StringComparison.Ordinal))
            throw new CryptographicException("Unknown PHI envelope version");
        var buffer = Convert.FromBase64String(protectedValue[Prefix.Length..]);
        var cipherLength = buffer.Length - 28;
        var plain = new byte[cipherLength];
        using var aes = new AesGcm(_encKey, 16);
        aes.Decrypt(buffer.AsSpan(0, 12), buffer.AsSpan(12, cipherLength), buffer.AsSpan(12 + cipherLength, 16), plain);
        return Encoding.UTF8.GetString(plain);
    }

    public string Hash(string value) =>
        Convert.ToHexString(HMACSHA256.HashData(_hashKey, Encoding.UTF8.GetBytes(value.Trim().ToUpperInvariant())));
}
