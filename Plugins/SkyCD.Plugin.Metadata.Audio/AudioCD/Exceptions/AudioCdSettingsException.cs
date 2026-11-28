using System;

namespace SkyCD.Plugin.Metadata.Audio.AudioCD.Exceptions;

/// <summary>
/// Exception thrown when persisted audio CD settings cannot be normalized.
/// </summary>
public sealed class AudioCdSettingsException : Exception
{
    private const string PredefinedMessage = "The audio CD settings are invalid.";

    public AudioCdSettingsException()
        : base(PredefinedMessage)
    {
    }

    public AudioCdSettingsException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public AudioCdSettingsException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
