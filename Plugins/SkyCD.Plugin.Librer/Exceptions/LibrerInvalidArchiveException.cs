using System;

namespace SkyCD.Plugin.Librer.Exceptions;

/// <summary>
/// Exception thrown when the file is not a valid Librer zip archive.
/// </summary>
public sealed class LibrerInvalidArchiveException : Exception
{
    private const string PredefinedMessage = "The file is not a valid Librer zip archive.";

    public LibrerInvalidArchiveException()
        : base(PredefinedMessage)
    {
    }

    public LibrerInvalidArchiveException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public LibrerInvalidArchiveException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
