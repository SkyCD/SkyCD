using System;

namespace SkyCD.Plugin.Audio.Exceptions;

/// <summary>
/// Exception thrown when a source claims an audio extension or MIME type but its container cannot be read.
/// </summary>
public sealed class AudioDecodeException : Exception
{
    private const string PredefinedMessage = "The audio source could not be read.";

    public AudioDecodeException()
        : base(PredefinedMessage)
    {
    }

    public AudioDecodeException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public AudioDecodeException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
