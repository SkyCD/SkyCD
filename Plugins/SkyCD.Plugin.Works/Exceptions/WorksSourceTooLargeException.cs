using System;

namespace SkyCD.Plugin.Works.Exceptions;

/// <summary>
/// Exception thrown when a source is larger than the plugin is willing to read.
/// </summary>
public sealed class WorksSourceTooLargeException : Exception
{
    private const string PredefinedMessage = "The Microsoft Works document of {0} bytes exceeds the {1} byte limit.";

    public WorksSourceTooLargeException(long lengthBytes, long limitBytes)
        : base(string.Format(PredefinedMessage, lengthBytes, limitBytes))
    {
    }
}
