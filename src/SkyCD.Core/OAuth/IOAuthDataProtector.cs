namespace SkyCD.Core.OAuth;

/// <summary>Host-provided OS-backed encryption. Implementations must never store a plaintext encryption key.</summary>
public interface IOAuthDataProtector
{
    byte[] Protect(byte[] plaintext, byte[] purpose);
    byte[] Unprotect(byte[] ciphertext, byte[] purpose);
}
