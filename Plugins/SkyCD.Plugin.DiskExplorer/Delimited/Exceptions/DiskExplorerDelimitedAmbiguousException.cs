using System;

namespace SkyCD.Plugin.DiskExplorer.Delimited.Exceptions;

public sealed class DiskExplorerDelimitedAmbiguousException(string reason)
    : InvalidOperationException($"Disk Explorer delimited-text dialect is ambiguous: {reason}")
{
}
