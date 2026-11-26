using System;

namespace SkyCD.Plugin.Metadata.Html.Exceptions;

/// <summary>
/// Exception thrown when a source claims to be HTML but cannot be decoded or parsed.
/// </summary>
public sealed class HtmlDecodeException : Exception
{
    private const string PredefinedMessage = "The HTML document could not be read.";

    public HtmlDecodeException()
        : base(PredefinedMessage)
    {
    }

    public HtmlDecodeException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public HtmlDecodeException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
