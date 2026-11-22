using System;

namespace SkyCD.Plugin.FileFormat.AbeMeda.Native.Exceptions;

/// <summary>
/// Thrown when a native catalog is truncated, internally inconsistent, or
/// declares ranges outside the source file.
/// </summary>
public sealed class AbeMedaNativeCorruptCatalogException(string detail)
    : InvalidOperationException($"Native abeMeda/NeoFinder catalog is corrupt or truncated: {detail}")
{
}
