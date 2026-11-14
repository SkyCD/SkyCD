using System;

namespace SkyCD.Plugin.CdPlayer.Ini.Exceptions;

/// <summary>
/// Exception thrown when a CDPlayer.ini file exceeds the maximum allowed file size.
/// </summary>
public sealed class CdPlayerFileSizeLimitExceededException : Exception
{
    private const string DefaultMessage = "CDPlayer.ini file exceeds the maximum allowed size limit of {0} bytes.";

    public CdPlayerFileSizeLimitExceededException(long limitBytes)
        : base(string.Format(DefaultMessage, limitBytes))
    {
    }

    public CdPlayerFileSizeLimitExceededException(long limitBytes, Exception innerException)
        : base(string.Format(DefaultMessage, limitBytes), innerException)
    {
    }
}
