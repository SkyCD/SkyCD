using System;

namespace SkyCD.Plugin.IWork.Exceptions;

/// <summary>
/// Exception thrown when a source claims to be an Apple iWork document but cannot be read.
/// </summary>
public sealed class IWorkDecodeException : Exception
{
    private const string PredefinedMessage = "The Apple iWork document could not be read.";

    public IWorkDecodeException()
        : base(PredefinedMessage)
    {
    }

    public IWorkDecodeException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public IWorkDecodeException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
