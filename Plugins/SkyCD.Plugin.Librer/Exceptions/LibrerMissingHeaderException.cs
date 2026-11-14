using System;

namespace SkyCD.Plugin.Librer.Exceptions;

/// <summary>
/// Exception thrown when the Librer catalog is missing a valid HEADER entry.
/// </summary>
public sealed class LibrerMissingHeaderException : Exception
{
    private const string PredefinedMessage = "The Librer catalog is missing a valid HEADER entry.";

    public LibrerMissingHeaderException()
        : base(PredefinedMessage)
    {
    }

    public LibrerMissingHeaderException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public LibrerMissingHeaderException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
