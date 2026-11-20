using System;

namespace SkyCD.Plugin.EComic.Exceptions;

/// <summary>
/// Exception thrown when a source claims a comic extension but its archive container cannot be read.
/// </summary>
public sealed class EComicArchiveException : Exception
{
    private const string PredefinedMessage = "The comic archive could not be read.";

    public EComicArchiveException()
        : base(PredefinedMessage)
    {
    }

    public EComicArchiveException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public EComicArchiveException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
