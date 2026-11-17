using System;

namespace SkyCD.Plugin.DiskExplorer.Exceptions;

public sealed class DiskExplorerEmptyReportException()
    : InvalidOperationException("Disk Explorer XML report contains no catalog entries.")
{
}
