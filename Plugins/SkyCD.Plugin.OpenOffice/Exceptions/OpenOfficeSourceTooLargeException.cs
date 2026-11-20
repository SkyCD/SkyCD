using System;

namespace SkyCD.Plugin.OpenOffice.Exceptions;

/// <summary>
/// Exception thrown when a source is larger than the plugin is willing to read.
/// </summary>
public sealed class OpenOfficeSourceTooLargeException : Exception
{
    private const string PredefinedMessage = "The OpenOffice document of {0} bytes exceeds the {1} byte limit.";

    public OpenOfficeSourceTooLargeException(long lengthBytes, long limitBytes)
        : base(string.Format(PredefinedMessage, lengthBytes, limitBytes))
    {
    }
}
