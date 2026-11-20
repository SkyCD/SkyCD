using System;

namespace SkyCD.Plugin.IWork.Exceptions;

/// <summary>
/// Exception thrown when a source is larger than the plugin is willing to read.
/// </summary>
public sealed class IWorkSourceTooLargeException : Exception
{
    private const string PredefinedMessage = "The Apple iWork document of {0} bytes exceeds the {1} byte limit.";

    public IWorkSourceTooLargeException(long lengthBytes, long limitBytes)
        : base(string.Format(PredefinedMessage, lengthBytes, limitBytes))
    {
    }
}
