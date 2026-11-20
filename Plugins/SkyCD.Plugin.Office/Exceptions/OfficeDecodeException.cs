using System;

namespace SkyCD.Plugin.Office.Exceptions;

/// <summary>
/// Exception thrown when a source claims to be a Microsoft Office document but cannot be read.
/// </summary>
public sealed class OfficeDecodeException : Exception
{
    private const string PredefinedMessage = "The Microsoft Office document could not be read.";

    public OfficeDecodeException()
        : base(PredefinedMessage)
    {
    }

    public OfficeDecodeException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public OfficeDecodeException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
