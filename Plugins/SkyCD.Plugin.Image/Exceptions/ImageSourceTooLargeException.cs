using System;

namespace SkyCD.Plugin.Image.Exceptions;

/// <summary>
/// Exception thrown when a source is larger than the plugin is willing to read.
/// </summary>
public sealed class ImageSourceTooLargeException : Exception
{
    private const string PredefinedMessage = "The image of {0} bytes exceeds the {1} byte limit.";

    public ImageSourceTooLargeException(long lengthBytes, long limitBytes)
        : base(string.Format(PredefinedMessage, lengthBytes, limitBytes))
    {
    }
}
