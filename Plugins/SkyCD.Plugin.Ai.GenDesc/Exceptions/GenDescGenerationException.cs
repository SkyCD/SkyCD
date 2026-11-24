using System;

namespace SkyCD.Plugin.Ai.GenDesc.Exceptions;

/// <summary>
/// Raised when a generation pass cannot produce a description even though it is enabled, for example when the
/// entry carries nothing to look up or the chosen template renders empty. It is distinct from a configuration
/// error so the host can surface a per-entry failure without implying the setup is broken.
/// </summary>
public sealed class GenDescGenerationException : Exception
{
    public GenDescGenerationException(string reason)
        : base($"GenDesc could not generate a description: {reason}.")
    {
    }
}
