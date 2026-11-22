using System;

namespace SkyCD.Plugin.Metadata.WordPerfect.Exceptions;

/// <summary>
/// Exception thrown when a source claims to be a WordPerfect document but cannot be read.
/// </summary>
public sealed class WordPerfectDecodeException : Exception
{
    private const string PredefinedMessage = "The WordPerfect document could not be read.";

    public WordPerfectDecodeException()
        : base(PredefinedMessage)
    {
    }

    public WordPerfectDecodeException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public WordPerfectDecodeException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
