using System;

namespace SkyCD.Plugin.Metadata.Executable.Exceptions;

/// <summary>
/// Exception thrown when a source claims to be an executable but cannot be read as one.
/// </summary>
/// <remarks>
/// A malformed, truncated or unsupported image is reported through this type so the plugin can answer with
/// "no metadata" instead of failing the whole indexing pass. Limits and cancellation use their own types and
/// are never wrapped into this one.
/// </remarks>
public sealed class ExecutableReadException : Exception
{
    private const string PredefinedMessage = "The executable file could not be read.";

    public ExecutableReadException()
        : base(PredefinedMessage)
    {
    }

    public ExecutableReadException(string details)
        : base($"{PredefinedMessage} Details: {details}")
    {
    }

    public ExecutableReadException(string details, Exception innerException)
        : base($"{PredefinedMessage} Details: {details}", innerException)
    {
    }
}
