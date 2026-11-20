using System;

namespace SkyCD.Plugin.WordPerfect.Exceptions;

/// <summary>
/// Exception thrown when a WordPerfect document is protected by a password the host does not hold.
/// </summary>
/// <remarks>
/// This is a distinct condition from a corrupt file, because the remedy differs: the document is intact and
/// readable, it just needs a credential. No password is ever requested, guessed, stored or logged.
/// </remarks>
public sealed class WordPerfectAuthenticationRequiredException : Exception
{
    private const string PredefinedMessage =
        "The WordPerfect document is password protected and cannot be read without a credential.";

    public WordPerfectAuthenticationRequiredException()
        : base(PredefinedMessage)
    {
    }

    public WordPerfectAuthenticationRequiredException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public WordPerfectAuthenticationRequiredException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
