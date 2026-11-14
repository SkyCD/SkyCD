using System;

namespace SkyCD.Plugin.CdPlayer.Ini.Exceptions;

/// <summary>
/// Exception thrown when a CDPlayer.ini file is invalid or contains no recognizable CD sections.
/// </summary>
public sealed class CdPlayerInvalidFormatException : Exception
{
    private const string DefaultMessage = "The provided file is not a valid CDPlayer.ini database: {0}.";

    public CdPlayerInvalidFormatException(string reason)
        : base(string.Format(DefaultMessage, reason))
    {
    }

    public CdPlayerInvalidFormatException(string reason, Exception innerException)
        : base(string.Format(DefaultMessage, reason), innerException)
    {
    }
}
