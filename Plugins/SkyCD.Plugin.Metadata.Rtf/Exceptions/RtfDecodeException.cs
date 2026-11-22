using System;

namespace SkyCD.Plugin.Metadata.Rtf.Exceptions;

/// <summary>
/// Exception thrown when a source claims to be an RTF document but cannot be read as one.
/// </summary>
public sealed class RtfDecodeException : Exception
{
    private const string PredefinedMessage = "The RTF document could not be read.";

    public RtfDecodeException()
        : base(PredefinedMessage)
    {
    }

    public RtfDecodeException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public RtfDecodeException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
