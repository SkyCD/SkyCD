using System;

namespace SkyCD.Plugin.DiskExplorer.Delimited.Exceptions;

public sealed class DiskExplorerDelimitedEmptyReportException()
    : InvalidOperationException("Disk Explorer delimited-text report contains no entries.")
{
}
