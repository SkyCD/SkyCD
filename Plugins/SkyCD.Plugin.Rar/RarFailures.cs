using System;
using System.IO;
using System.Security.Cryptography;

namespace SkyCD.Plugin.Rar;

/// <summary>
/// Classification of archive errors into the typed RAR diagnostic codes the plugin reports.
/// </summary>
internal static class RarFailures
{
    /// <summary>Diagnostic for archives that cannot be parsed or fail their integrity checks.</summary>
    public const string DamagedCode = "RAR_DAMAGED";

    /// <summary>Diagnostic for archives whose headers or entries are encrypted.</summary>
    public const string EncryptedCode = "RAR_ENCRYPTED";

    /// <summary>Diagnostic for archives that use features the reader cannot decode.</summary>
    public const string UnsupportedCode = "RAR_UNSUPPORTED";

    /// <summary>Diagnostic for archives that need companion volumes.</summary>
    public const string MultivolumeCode = "RAR_MULTIVOLUME";

    /// <summary>Returns whether the exception describes an archive level failure the plugin can report.</summary>
    public static bool IsArchiveFailure(Exception exception) =>
        exception is SharpCompress.Common.SharpCompressException or IOException or NotSupportedException
            or ArgumentException or InvalidOperationException or UnauthorizedAccessException;

    /// <summary>Returns the typed diagnostic code that describes <paramref name="exception"/>.</summary>
    public static string CodeFor(Exception exception) => exception switch
    {
        CryptographicException => EncryptedCode,
        SharpCompress.Common.CryptographicException => EncryptedCode,
        NotSupportedException => UnsupportedCode,
        SharpCompress.Common.MultiVolumeExtractionException => MultivolumeCode,
        _ => DamagedCode
    };

    /// <summary>Formats a typed diagnostic that carries the original exception message.</summary>
    public static string Format(Exception exception) => Format(CodeFor(exception), exception.Message);

    /// <summary>Formats a typed diagnostic from a code and message.</summary>
    public static string Format(string code, string message) => $"{code}: {message}";
}
