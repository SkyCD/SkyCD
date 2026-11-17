using System;

namespace SkyCD.Plugin.GCstar.Exceptions;

public sealed class GCstarUnsupportedSchemaException(string? reason = null)
    : InvalidOperationException(
        string.IsNullOrWhiteSpace(reason)
            ? "XML document is not a GCstar collection export."
            : $"XML document is not a GCstar collection export: {reason}")
{
}
