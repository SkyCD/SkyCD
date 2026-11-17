using System;

namespace SkyCD.Plugin.DiskExplorer.Html.Exceptions;

public sealed class DiskExplorerHtmlEmptyReportException()
    : InvalidOperationException("Disk Explorer HTML report contains no catalog entries.")
{
}
