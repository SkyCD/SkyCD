using System;

namespace SkyCD.Plugin.Metadata.Image.Exceptions;

/// <summary>
/// Exception thrown when a source claims an image extension or MIME type but cannot be decoded.
/// </summary>
public sealed class ImageDecodeException : Exception
{
    private const string PredefinedMessage = "The image could not be decoded.";

    public ImageDecodeException()
        : base(PredefinedMessage)
    {
    }

    public ImageDecodeException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public ImageDecodeException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
