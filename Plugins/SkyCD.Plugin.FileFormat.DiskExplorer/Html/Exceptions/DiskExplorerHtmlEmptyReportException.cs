using System;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Html.Exceptions;

public sealed class DiskExplorerHtmlEmptyReportException()
    : InvalidOperationException("Disk Explorer HTML report contains no catalog entries.")
{
}
