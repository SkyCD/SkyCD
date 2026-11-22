using System;

namespace SkyCD.Plugin.Metadata.Text.Exceptions;

/// <summary>
/// Exception thrown when a source on the text allow-list cannot be decoded as text.
/// </summary>
public sealed class TextDecodeException : Exception
{
    private const string PredefinedMessage = "The text source could not be read.";

    public TextDecodeException()
        : base(PredefinedMessage)
    {
    }

    public TextDecodeException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public TextDecodeException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
