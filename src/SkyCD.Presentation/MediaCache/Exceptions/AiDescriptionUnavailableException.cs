using System;

namespace SkyCD.Presentation.MediaCache.Exceptions;

/// <summary>Explicit generation cannot start because no provider is installed or its configuration is disabled.</summary>
public sealed class AiDescriptionUnavailableException : Exception
{
    public AiDescriptionUnavailableException(string reason)
        : base($"AI description generation is unavailable: {reason}")
    {
    }
}
