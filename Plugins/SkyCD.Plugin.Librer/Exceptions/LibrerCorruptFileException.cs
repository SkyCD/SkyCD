using System;

namespace SkyCD.Plugin.Librer.Exceptions;

/// <summary>
/// Exception thrown when a Librer catalog file is corrupt or cannot be read.
/// </summary>
public sealed class LibrerCorruptFileException : Exception
{
    private const string PredefinedMessage = "The Librer catalog file is corrupt or incomplete.";

    public LibrerCorruptFileException()
        : base(PredefinedMessage)
    {
    }

    public LibrerCorruptFileException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public LibrerCorruptFileException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
