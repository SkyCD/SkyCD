using System;
using System.IO;
using SkyCD.Plugin.Ace.Exceptions;

namespace SkyCD.Plugin.Ace.Exceptions;

/// <summary>
/// Classifies exceptions that mean the archive is damaged or unsupported rather than the host being
/// misused, so a failed scan contributes no entries instead of failing the whole indexing pass.
/// </summary>
internal static class AceArchiveFailures
{
    /// <summary>Gets the typed failure code reported for this exception.</summary>
    public static string GetFailureCode(Exception exception) => exception switch
    {
        AceEncryptedArchiveException => "ACE_ENCRYPTED",
        AceUnsupportedBlockException => "ACE_UNSUPPORTED",
        _ => "ACE_DAMAGED"
    };

    public static bool IsArchiveFailure(Exception exception) =>
        exception is AceArchiveFormatException or AceEncryptedArchiveException
            or AceUnsupportedBlockException or AceEntryLimitExceededException or InvalidDataException
            or IOException or NotSupportedException or ArgumentException or UnauthorizedAccessException
            or InvalidOperationException or FormatException or OverflowException;
}
