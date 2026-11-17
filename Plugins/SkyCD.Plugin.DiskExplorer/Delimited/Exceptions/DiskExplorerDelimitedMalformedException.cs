using System;

namespace SkyCD.Plugin.DiskExplorer.Delimited.Exceptions;

public sealed class DiskExplorerDelimitedMalformedException(string reason)
    : InvalidOperationException($"Disk Explorer delimited-text report is malformed: {reason}")
{
}
