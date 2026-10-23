using System;
using System.Security.Cryptography;
using SkyCD.Plugin.Abstractions.OAuth;

namespace SkyCD.Core.OAuth;

/// <summary>Windows DPAPI encryption bound to the current OS user, without an application-managed key.</summary>
public sealed class WindowsOAuthDataProtector : IOAuthDataProtector
{
    public byte[] Protect(byte[] plaintext, byte[] purpose)
    {
        if (!OperatingSystem.IsWindows())
            throw new OAuthException(OAuthFailure.SecureStorageUnavailable);
        try { return ProtectedData.Protect(plaintext, purpose, DataProtectionScope.CurrentUser); }
        catch (CryptographicException) { throw new OAuthException(OAuthFailure.SecureStorageUnavailable); }
    }

    public byte[] Unprotect(byte[] ciphertext, byte[] purpose)
    {
        if (!OperatingSystem.IsWindows())
            throw new OAuthException(OAuthFailure.SecureStorageUnavailable);
        return ProtectedData.Unprotect(ciphertext, purpose, DataProtectionScope.CurrentUser);
    }
}
