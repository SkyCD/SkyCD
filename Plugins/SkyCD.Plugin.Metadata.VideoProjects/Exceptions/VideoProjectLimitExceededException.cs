using System;

namespace SkyCD.Plugin.Metadata.VideoProjects.Exceptions;

/// <summary>An untrusted project asked the reader to exceed one of its fixed bounds.</summary>
public sealed class VideoProjectLimitExceededException : Exception
{
    public VideoProjectLimitExceededException(string resource, long limit)
        : base($"The video project file exceeds the {resource} limit of {limit}.")
    {
    }
}
