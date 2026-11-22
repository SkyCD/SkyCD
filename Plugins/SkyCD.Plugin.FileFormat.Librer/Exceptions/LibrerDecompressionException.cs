using System;

namespace SkyCD.Plugin.FileFormat.Librer.Exceptions;

/// <summary>
/// Exception thrown when decompressing a Librer archive entry fails.
/// </summary>
public sealed class LibrerDecompressionException : Exception
{
    private const string PredefinedMessage = "Failed to decompress Librer archive entry: '{0}'.";

    public LibrerDecompressionException(string entryName)
        : base(string.Format(PredefinedMessage, entryName))
    {
    }

    public LibrerDecompressionException(string entryName, Exception innerException)
        : base(string.Format(PredefinedMessage, entryName), innerException)
    {
    }
}
