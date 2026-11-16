using System;

namespace SkyCD.Plugin.Pdf.Exceptions;

/// <summary>
/// Exception thrown when a source claims to be a PDF but cannot be parsed.
/// </summary>
public sealed class PdfDecodeException : Exception
{
    private const string PredefinedMessage = "The PDF document could not be read.";

    public PdfDecodeException()
        : base(PredefinedMessage)
    {
    }

    public PdfDecodeException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public PdfDecodeException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
