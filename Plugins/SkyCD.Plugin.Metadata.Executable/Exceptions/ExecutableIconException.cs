using System;

namespace SkyCD.Plugin.Metadata.Executable.Exceptions;

/// <summary>
/// Exception thrown when an executable carries icon resources that cannot be turned into an image.
/// </summary>
/// <remarks>
/// Missing, damaged and unsupported icon resources all end here; the icon capability turns it into an empty
/// artifact list, the explicit "no icon" state, so a broken icon never hides the rest of the metadata.
/// </remarks>
public sealed class ExecutableIconException : Exception
{
    private const string PredefinedMessage = "The executable's icon could not be extracted.";

    public ExecutableIconException()
        : base(PredefinedMessage)
    {
    }

    public ExecutableIconException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public ExecutableIconException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
