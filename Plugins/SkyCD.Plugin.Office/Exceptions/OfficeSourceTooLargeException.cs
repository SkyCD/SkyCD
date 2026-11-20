using System;

namespace SkyCD.Plugin.Office.Exceptions;

/// <summary>
/// Exception thrown when a source is larger than the plugin is willing to read.
/// </summary>
public sealed class OfficeSourceTooLargeException : Exception
{
    private const string PredefinedMessage = "The Microsoft Office document of {0} bytes exceeds the {1} byte limit.";

    public OfficeSourceTooLargeException(long lengthBytes, long limitBytes)
        : base(string.Format(PredefinedMessage, lengthBytes, limitBytes))
    {
    }
}
