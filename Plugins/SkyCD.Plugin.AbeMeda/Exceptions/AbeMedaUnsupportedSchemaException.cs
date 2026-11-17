using System;

namespace SkyCD.Plugin.AbeMeda.Exceptions;

public sealed class AbeMedaUnsupportedSchemaException(string? reason = null)
    : InvalidOperationException(
        string.IsNullOrWhiteSpace(reason)
            ? "XML document is not a supported abeMeda/NeoFinder-family catalog export."
            : $"XML document is not a supported abeMeda/NeoFinder-family catalog export: {reason}")
{
}
