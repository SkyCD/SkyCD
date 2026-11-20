using System;

namespace SkyCD.Plugin.EBook.Exceptions;

/// <summary>
/// Exception thrown when an embedded cover image cannot be turned into an encoded thumbnail.
/// </summary>
public sealed class EBookCoverException : Exception
{
    private const string PredefinedMessage = "The e-book cover could not be produced.";

    public EBookCoverException()
        : base(PredefinedMessage)
    {
    }

    public EBookCoverException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public EBookCoverException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
