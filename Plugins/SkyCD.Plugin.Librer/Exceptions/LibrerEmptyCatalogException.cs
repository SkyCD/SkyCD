using System;

namespace SkyCD.Plugin.Librer.Exceptions;

/// <summary>
/// Exception thrown when the Librer catalog contains no files or folders.
/// </summary>
public sealed class LibrerEmptyCatalogException : Exception
{
    private const string PredefinedMessage = "The Librer catalog contains no files or folders.";

    public LibrerEmptyCatalogException()
        : base(PredefinedMessage)
    {
    }

    public LibrerEmptyCatalogException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public LibrerEmptyCatalogException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
