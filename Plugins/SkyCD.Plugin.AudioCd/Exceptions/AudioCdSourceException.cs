using System;

namespace SkyCD.Plugin.AudioCd.Exceptions;

/// <summary>
/// Exception thrown when an online metadata source returns a response that cannot be understood.
/// </summary>
public sealed class AudioCdSourceException : Exception
{
    private const string PredefinedMessage = "An audio CD metadata source returned an unusable response.";

    public AudioCdSourceException()
        : base(PredefinedMessage)
    {
    }

    public AudioCdSourceException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public AudioCdSourceException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
