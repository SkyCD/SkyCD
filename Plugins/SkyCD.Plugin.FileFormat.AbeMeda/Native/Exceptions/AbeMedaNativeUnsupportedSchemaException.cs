using System;

namespace SkyCD.Plugin.FileFormat.AbeMeda.Native.Exceptions;

/// <summary>
/// Thrown when the source does not carry the abeMeda/NeoFinder native signature.
/// </summary>
public sealed class AbeMedaNativeUnsupportedSchemaException(string? reason = null)
    : InvalidOperationException(
        string.IsNullOrWhiteSpace(reason)
            ? "File is not a supported abeMeda/NeoFinder native catalog."
            : $"File is not a supported abeMeda/NeoFinder native catalog: {reason}")
{
}
