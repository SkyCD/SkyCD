using System;

namespace SkyCD.Plugin.Metadata.IWork.Exceptions;

/// <summary>
/// Exception thrown when an Apple iWork document uses a generation this plugin does not read.
/// </summary>
/// <remarks>
/// Legacy iWork '09 packages store an XML or APXL index rather than the modern IWA component set. They are
/// reported distinctly from damage because the file itself is intact, just not in the supported generation.
/// </remarks>
public sealed class IWorkUnsupportedFormatException : Exception
{
    private const string PredefinedMessage = "The Apple iWork document uses an unsupported legacy format.";

    public IWorkUnsupportedFormatException()
        : base(PredefinedMessage)
    {
    }

    public IWorkUnsupportedFormatException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public IWorkUnsupportedFormatException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
