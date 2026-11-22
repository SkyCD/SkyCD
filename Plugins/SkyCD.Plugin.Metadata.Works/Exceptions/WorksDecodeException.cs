using System;

namespace SkyCD.Plugin.Metadata.Works.Exceptions;

/// <summary>
/// Exception thrown when a source claims to be a Microsoft Works document but cannot be read.
/// </summary>
public sealed class WorksDecodeException : Exception
{
    private const string PredefinedMessage = "The Microsoft Works document could not be read.";

    public WorksDecodeException()
        : base(PredefinedMessage)
    {
    }

    public WorksDecodeException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public WorksDecodeException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
