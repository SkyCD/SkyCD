using System;

namespace SkyCD.Plugin.Pdf.Exceptions;

/// <summary>
/// Exception thrown when a source is larger than the plugin is willing to read.
/// </summary>
public sealed class PdfSourceTooLargeException : Exception
{
    private const string PredefinedMessage = "The PDF document of {0} bytes exceeds the {1} byte limit.";

    public PdfSourceTooLargeException(long lengthBytes, long limitBytes)
        : base(string.Format(PredefinedMessage, lengthBytes, limitBytes))
    {
    }
}
