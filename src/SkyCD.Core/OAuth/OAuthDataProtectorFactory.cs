using System;
using System.Runtime.InteropServices;
using SkyCD.Plugin.Abstractions.OAuth;

namespace SkyCD.Core.OAuth;

public static class OAuthDataProtectorFactory
{
    public static IOAuthDataProtector Create() => Create(
        OperatingSystem.IsWindows() ? OSPlatform.Windows :
        OperatingSystem.IsMacOS() ? OSPlatform.OSX :
        OperatingSystem.IsLinux() ? OSPlatform.Linux : OSPlatform.Create("Unsupported"));

    internal static IOAuthDataProtector Create(OSPlatform platform)
    {
        if (platform == OSPlatform.Windows)
            return new WindowsOAuthDataProtector();
        if (platform == OSPlatform.OSX)
            return new KeyringOAuthDataProtector(new MacOSOAuthKeyStore());
        if (platform == OSPlatform.Linux)
            return new KeyringOAuthDataProtector(new LinuxOAuthKeyStore());
        throw new OAuthException(OAuthFailure.SecureStorageUnavailable);
    }
}
