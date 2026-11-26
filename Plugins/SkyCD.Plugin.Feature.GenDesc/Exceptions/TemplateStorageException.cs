using System;

namespace SkyCD.Plugin.Feature.GenDesc.Exceptions;

/// <summary>Raised when a template file cannot be read, written or named safely.</summary>
public sealed class TemplateStorageException : Exception
{
    public TemplateStorageException(string detail)
        : base($"Template storage error: {detail}.")
    {
    }
}
