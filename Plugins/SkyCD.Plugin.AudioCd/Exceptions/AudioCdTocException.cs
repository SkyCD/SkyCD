using System;

namespace SkyCD.Plugin.AudioCd.Exceptions;

/// <summary>
/// Exception thrown when the table of contents of an audio CD cannot be read from the drive.
/// </summary>
public sealed class AudioCdTocException : Exception
{
    private const string PredefinedMessage = "The audio CD table of contents could not be read.";

    public AudioCdTocException()
        : base(PredefinedMessage)
    {
    }

    public AudioCdTocException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public AudioCdTocException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
