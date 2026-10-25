using System;
using System.Security.Cryptography;
using SkyCD.Plugin.Abstractions.OAuth;

namespace SkyCD.Core.OAuth;

/// <summary>AES-256-GCM session encryption with a namespace key held exclusively by the OS keyring.</summary>
internal sealed class KeyringOAuthDataProtector(IOAuthKeyStore keys) : IOAuthDataProtector
{
    // Serializes first-key creation across protector instances within the supported single host process.
    private static readonly object Gate = new();
    private const int HeaderLength = 1 + 12 + 16;

    public byte[] Protect(byte[] plaintext, byte[] purpose) => WithKey(purpose, true, key =>
    {
        var result = new byte[HeaderLength + plaintext.Length];
        result[0] = 1;
        RandomNumberGenerator.Fill(result.AsSpan(1, 12));
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(result.AsSpan(1, 12), plaintext, result.AsSpan(HeaderLength), result.AsSpan(13, 16), purpose);
        return result;
    });

    public byte[] Unprotect(byte[] ciphertext, byte[] purpose)
    {
        if (ciphertext.Length < HeaderLength || ciphertext[0] != 1)
            throw new OAuthException(OAuthFailure.SecureStorageUnavailable);
        return WithKey(purpose, false, key =>
        {
            var result = new byte[ciphertext.Length - HeaderLength];
            try
            {
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(ciphertext.AsSpan(1, 12), ciphertext.AsSpan(HeaderLength), ciphertext.AsSpan(13, 16), result, purpose);
                return result;
            }
            catch
            {
                CryptographicOperations.ZeroMemory(result);
                throw;
            }
        });
    }

    private byte[] WithKey(byte[] purpose, bool create, Func<byte[], byte[]> action)
    {
        byte[]? key = null;
        try
        {
            lock (Gate)
            {
                var keyId = Convert.ToHexString(SHA256.HashData(purpose));
                key = keys.Read(keyId);
                if (key is null && create)
                {
                    var generated = RandomNumberGenerator.GetBytes(32);
                    try { keys.Create(keyId, generated); }
                    finally { CryptographicOperations.ZeroMemory(generated); }
                    // Do not publish ciphertext until the key is durably retrievable from the OS.
                    key = keys.Read(keyId);
                }
            }
            if (key is null || key.Length != 32)
                throw new OAuthException(OAuthFailure.SecureStorageUnavailable);
            return action(key);
        }
        catch (Exception)
        {
            // No native error text, key material, or inner exception may escape this boundary.
            throw new OAuthException(OAuthFailure.SecureStorageUnavailable);
        }
        finally
        {
            if (key is not null)
                CryptographicOperations.ZeroMemory(key);
        }
    }
}
