using System;

namespace SkyCD.Plugin.Feature.GenDesc.Exceptions;

/// <summary>
/// Raised when a generation pass cannot produce an output even though the template is valid, for example
/// when no template covers the kind or the selected template renders empty.
/// </summary>
public sealed class GenDescGenerationException : Exception
{
    public GenDescGenerationException(string reason)
        : base($"GenDesc could not generate output: {reason}.")
    {
    }
}
