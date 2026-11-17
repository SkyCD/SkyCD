using System;

namespace SkyCD.Plugin.DiskExplorer.Exceptions;

public sealed class DiskExplorerMalformedDocumentException(string reason, Exception? inner = null)
    : InvalidOperationException($"Disk Explorer XML report is malformed: {reason}", inner)
{
}
