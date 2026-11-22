using System;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Delimited.Exceptions;

public sealed class DiskExplorerDelimitedMalformedException(string reason)
    : InvalidOperationException($"Disk Explorer delimited-text report is malformed: {reason}")
{
}
