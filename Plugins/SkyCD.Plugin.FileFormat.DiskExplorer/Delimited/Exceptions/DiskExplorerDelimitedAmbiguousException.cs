using System;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Delimited.Exceptions;

public sealed class DiskExplorerDelimitedAmbiguousException(string reason)
    : InvalidOperationException($"Disk Explorer delimited-text dialect is ambiguous: {reason}")
{
}
