using System;

namespace SkyCD.Plugin.Video.Exceptions;

/// <summary>
/// Exception thrown when a source claims a video extension or MIME type but its container cannot be read.
/// </summary>
public sealed class VideoDecodeException : Exception
{
    private const string PredefinedMessage = "The video source could not be read.";

    public VideoDecodeException()
        : base(PredefinedMessage)
    {
    }

    public VideoDecodeException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public VideoDecodeException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
