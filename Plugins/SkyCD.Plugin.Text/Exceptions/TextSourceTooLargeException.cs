using System;

namespace SkyCD.Plugin.Text.Exceptions;

/// <summary>
/// Exception thrown when a source is larger than the plugin is willing to read.
/// </summary>
public sealed class TextSourceTooLargeException : Exception
{
    private const string PredefinedMessage = "The text source of {0} bytes exceeds the {1} byte limit.";

    public TextSourceTooLargeException(long lengthBytes, long limitBytes)
        : base(string.Format(PredefinedMessage, lengthBytes, limitBytes))
    {
    }
}
