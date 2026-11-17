using System;

namespace SkyCD.Plugin.DiskExplorer.Exceptions;

public sealed class DiskExplorerUnsupportedSchemaException(string? reason = null)
    : InvalidOperationException(
        string.IsNullOrWhiteSpace(reason)
            ? "XML document is not a Disk Explorer Professional report."
            : $"XML document is not a Disk Explorer Professional report: {reason}")
{
}
