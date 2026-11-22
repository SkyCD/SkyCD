using System;

namespace SkyCD.Plugin.Metadata.EComic.Exceptions;

/// <summary>
/// Exception thrown when a page decoded from a comic archive cannot be turned into an encoded cover.
/// </summary>
public sealed class EComicCoverException : Exception
{
    private const string PredefinedMessage = "The comic cover could not be produced.";

    public EComicCoverException()
        : base(PredefinedMessage)
    {
    }

    public EComicCoverException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public EComicCoverException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
