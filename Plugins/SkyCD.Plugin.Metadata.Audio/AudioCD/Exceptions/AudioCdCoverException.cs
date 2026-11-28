using System;

namespace SkyCD.Plugin.Metadata.Audio.AudioCD.Exceptions;

/// <summary>
/// Exception thrown when cover art cannot be decoded, scaled or encoded.
/// </summary>
public sealed class AudioCdCoverException : Exception
{
    private const string PredefinedMessage = "The audio CD cover art could not be rendered.";

    public AudioCdCoverException()
        : base(PredefinedMessage)
    {
    }

    public AudioCdCoverException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public AudioCdCoverException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
