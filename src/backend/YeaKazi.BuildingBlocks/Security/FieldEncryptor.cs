using System.Security.Cryptography;
using System.Text;

namespace YeaKazi.BuildingBlocks.Security;

/// <summary>Encrypts secrets stored in the database (e.g. TOTP seeds) with AES-256-GCM.</summary>
public interface IFieldEncryptor
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
}

/// <summary>
/// Format: "{keyId}:{base64(nonce|ciphertext|tag)}". Several keys may be configured
/// so that old values stay readable after rotation; new values use the active key.
/// </summary>
public sealed class AesGcmFieldEncryptor : IFieldEncryptor
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly string _activeKeyId;
    private readonly IReadOnlyDictionary<string, byte[]> _keys;

    public AesGcmFieldEncryptor(string activeKeyId, IReadOnlyDictionary<string, string> base64Keys)
    {
        if (!base64Keys.ContainsKey(activeKeyId))
        {
            throw new InvalidOperationException("The active field-encryption key is not configured.");
        }

        _activeKeyId = activeKeyId;
        _keys = base64Keys.ToDictionary(kv => kv.Key, kv =>
        {
            var key = Convert.FromBase64String(kv.Value);
            if (key.Length != 32)
            {
                throw new InvalidOperationException($"Field-encryption key '{kv.Key}' must be 32 bytes.");
            }

            return key;
        });
    }

    public string Encrypt(string plaintext)
    {
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var buffer = new byte[NonceSize + plain.Length + TagSize];
        var nonce = buffer.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_keys[_activeKeyId], TagSize);
        aes.Encrypt(nonce, plain, buffer.AsSpan(NonceSize, plain.Length), buffer.AsSpan(NonceSize + plain.Length, TagSize),
            Encoding.UTF8.GetBytes(_activeKeyId));
        return $"{_activeKeyId}:{Convert.ToBase64String(buffer)}";
    }

    public string Decrypt(string ciphertext)
    {
        var sep = ciphertext.IndexOf(':');
        if (sep <= 0)
        {
            throw new CryptographicException("Malformed encrypted value.");
        }

        var keyId = ciphertext[..sep];
        if (!_keys.TryGetValue(keyId, out var key))
        {
            throw new CryptographicException("Unknown encryption key id.");
        }

        var buffer = Convert.FromBase64String(ciphertext[(sep + 1)..]);
        var cipherLength = buffer.Length - NonceSize - TagSize;
        var plain = new byte[cipherLength];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(buffer.AsSpan(0, NonceSize), buffer.AsSpan(NonceSize, cipherLength), buffer.AsSpan(NonceSize + cipherLength, TagSize),
            plain, Encoding.UTF8.GetBytes(keyId));
        return Encoding.UTF8.GetString(plain);
    }
}
