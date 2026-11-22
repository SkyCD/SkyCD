using System;

namespace SkyCD.Plugin.Metadata.EBook.Exceptions;

/// <summary>
/// Exception thrown when a source claims an e-book extension but its bytes cannot be recognized or read as
/// one of the supported formats.
/// </summary>
public sealed class EBookDecodeException : Exception
{
    private const string PredefinedMessage = "The e-book could not be read.";

    public EBookDecodeException()
        : base(PredefinedMessage)
    {
    }

    public EBookDecodeException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public EBookDecodeException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
