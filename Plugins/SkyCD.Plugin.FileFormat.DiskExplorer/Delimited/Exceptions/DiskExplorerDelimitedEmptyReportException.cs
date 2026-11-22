using System;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Delimited.Exceptions;

public sealed class DiskExplorerDelimitedEmptyReportException()
    : InvalidOperationException("Disk Explorer delimited-text report contains no entries.")
{
}
