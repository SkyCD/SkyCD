using System;

namespace SkyCD.Plugin.Metadata.Pdf.Exceptions;

/// <summary>
/// Exception thrown when a PDF is protected by a password the host does not hold.
/// </summary>
/// <remarks>
/// This is a distinct condition from a corrupt file, because the remedy differs: the document is intact and
/// readable, it just needs a credential. No password is ever requested, guessed, stored or logged.
/// </remarks>
public sealed class PdfAuthenticationRequiredException : Exception
{
    private const string PredefinedMessage =
        "The PDF document is password protected and cannot be read without a credential.";

    public PdfAuthenticationRequiredException()
        : base(PredefinedMessage)
    {
    }

    public PdfAuthenticationRequiredException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public PdfAuthenticationRequiredException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
