using System;

namespace SkyCD.Plugin.Metadata.VideoProjects.Exceptions;

/// <summary>The project container was recognized but its content is damaged or not the expected structure.</summary>
public sealed class VideoProjectDecodeException : Exception
{
    public VideoProjectDecodeException(string reason, Exception? innerException = null)
        : base($"The video project file could not be read. Reason: {reason}.", innerException)
    {
    }
}
