using System;

namespace SkyCD.Plugin.Metadata.Markdown.Exceptions;

/// <summary>
/// Exception thrown when a source is larger than the plugin is willing to read.
/// </summary>
public sealed class MarkdownSourceTooLargeException : Exception
{
    private const string PredefinedMessage = "The Markdown document of {0} bytes exceeds the {1} byte limit.";

    public MarkdownSourceTooLargeException(long lengthBytes, long limitBytes)
        : base(string.Format(PredefinedMessage, lengthBytes, limitBytes))
    {
    }
}
