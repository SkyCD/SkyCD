using System;

namespace SkyCD.Plugin.Rtf.Exceptions;

/// <summary>
/// Exception thrown when a source is larger than the plugin is willing to read.
/// </summary>
public sealed class RtfSourceTooLargeException : Exception
{
    private const string PredefinedMessage = "The RTF document of {0} bytes exceeds the {1} byte limit.";

    public RtfSourceTooLargeException(long lengthBytes, long limitBytes)
        : base(string.Format(PredefinedMessage, lengthBytes, limitBytes))
    {
    }
}
