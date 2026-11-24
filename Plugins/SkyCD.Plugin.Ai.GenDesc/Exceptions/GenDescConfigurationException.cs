using System;

namespace SkyCD.Plugin.Ai.GenDesc.Exceptions;

/// <summary>Raised when the GenDesc configuration file is missing, malformed, or out of bounds.</summary>
public sealed class GenDescConfigurationException : Exception
{
    public GenDescConfigurationException(string reason)
        : base($"GenDesc configuration is unusable: {reason}.")
    {
    }
}
