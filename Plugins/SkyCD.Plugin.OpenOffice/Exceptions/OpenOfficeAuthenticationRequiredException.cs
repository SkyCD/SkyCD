using System;

namespace SkyCD.Plugin.OpenOffice.Exceptions;

/// <summary>
/// Exception thrown when an OpenOffice package is protected by a password the host does not hold.
/// </summary>
/// <remarks>
/// This is a distinct condition from a corrupt file, because the remedy differs: the package is intact and
/// readable, it just needs a credential. No password is ever requested, guessed, stored or logged.
/// </remarks>
public sealed class OpenOfficeAuthenticationRequiredException : Exception
{
    private const string PredefinedMessage =
        "The OpenOffice document is password protected and cannot be read without a credential.";

    public OpenOfficeAuthenticationRequiredException()
        : base(PredefinedMessage)
    {
    }

    public OpenOfficeAuthenticationRequiredException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public OpenOfficeAuthenticationRequiredException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
