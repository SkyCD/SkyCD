using System;

namespace SkyCD.Plugin.Metadata.Works.Exceptions;

/// <summary>
/// Exception thrown when a source is wrapped in an OLE encryption container and needs a credential.
/// </summary>
public sealed class WorksAuthenticationRequiredException : Exception
{
    private const string PredefinedMessage = "The Microsoft Works document is password protected.";

    public WorksAuthenticationRequiredException()
        : base(PredefinedMessage)
    {
    }

    public WorksAuthenticationRequiredException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public WorksAuthenticationRequiredException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
