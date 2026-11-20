using System;

namespace SkyCD.Plugin.WordPerfect.Exceptions;

/// <summary>
/// Exception thrown when a source is larger than the plugin is willing to read.
/// </summary>
public sealed class WordPerfectSourceTooLargeException : Exception
{
    private const string PredefinedMessage = "The WordPerfect document of {0} bytes exceeds the {1} byte limit.";

    public WordPerfectSourceTooLargeException(long lengthBytes, long limitBytes)
        : base(string.Format(PredefinedMessage, lengthBytes, limitBytes))
    {
    }
}
