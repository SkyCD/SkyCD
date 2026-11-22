using System;

namespace SkyCD.Plugin.FileFormat.CDPlayer.Ini.Exceptions;

/// <summary>
/// Exception thrown when decoding a CDPlayer.ini file encounters invalid or malformed character sequences.
/// </summary>
public sealed class CdPlayerEncodingException : Exception
{
    private const string DefaultMessage = "Failed to decode CDPlayer.ini text using encoding '{0}': {1}.";

    public CdPlayerEncodingException(string encodingName, string details)
        : base(string.Format(DefaultMessage, encodingName, details))
    {
    }

    public CdPlayerEncodingException(string encodingName, string details, Exception innerException)
        : base(string.Format(DefaultMessage, encodingName, details), innerException)
    {
    }
}
