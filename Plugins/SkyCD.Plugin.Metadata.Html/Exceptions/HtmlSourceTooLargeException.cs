using System;

namespace SkyCD.Plugin.Metadata.Html.Exceptions;

/// <summary>
/// Exception thrown when a source is larger than the plugin is willing to read.
/// </summary>
public sealed class HtmlSourceTooLargeException : Exception
{
    private const string PredefinedMessage = "The HTML document of {0} bytes exceeds the {1} byte limit.";

    public HtmlSourceTooLargeException(long lengthBytes, long limitBytes)
        : base(string.Format(PredefinedMessage, lengthBytes, limitBytes))
    {
    }
}
