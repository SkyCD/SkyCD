using System;

namespace SkyCD.Plugin.Metadata.OpenOffice.Exceptions;

/// <summary>
/// Exception thrown when a source claims to be an OpenOffice document but cannot be read.
/// </summary>
public sealed class OpenOfficeDecodeException : Exception
{
    private const string PredefinedMessage = "The OpenOffice document could not be read.";

    public OpenOfficeDecodeException()
        : base(PredefinedMessage)
    {
    }

    public OpenOfficeDecodeException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public OpenOfficeDecodeException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
