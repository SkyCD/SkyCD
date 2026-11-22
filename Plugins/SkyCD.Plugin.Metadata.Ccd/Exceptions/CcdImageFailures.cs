using System;
using System.IO;

namespace SkyCD.Plugin.Metadata.Ccd.Exceptions;

/// <summary>
/// Classifies exceptions that mean the image is damaged or unsupported rather than the host being
/// misused, so a failed scan contributes no entries instead of failing the whole indexing pass.
/// </summary>
internal static class CcdImageFailures
{
    public static bool IsImageFailure(Exception exception) =>
        exception is IOException or InvalidDataException or NotSupportedException or ArgumentException
            or UnauthorizedAccessException or InvalidOperationException or FormatException or OverflowException;
}
