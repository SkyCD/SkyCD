using System;

namespace SkyCD.Plugin.AbeMeda.Native.Exceptions;

/// <summary>
/// Thrown for a recognized but not yet verified catalog generation
/// (the extended <c>.abeMeda7</c>/<c>.neofinder7</c> format).
/// </summary>
public sealed class AbeMedaNativeUnsupportedGenerationException(string generation)
    : InvalidOperationException(
        $"Native abeMeda/NeoFinder {generation} catalogs are recognized but not yet importable. " +
        "Item parsing stays disabled until verified fixtures confirm the extended layout.")
{
}
