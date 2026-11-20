using System;

namespace SkyCD.Plugin.Markdown.Metadata.Exceptions;

/// <summary>
/// Exception thrown when a source claims to be Markdown but cannot be decoded as text.
/// </summary>
public sealed class MarkdownDecodeException : Exception
{
    private const string PredefinedMessage = "The Markdown document could not be read.";

    public MarkdownDecodeException()
        : base(PredefinedMessage)
    {
    }

    public MarkdownDecodeException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public MarkdownDecodeException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
