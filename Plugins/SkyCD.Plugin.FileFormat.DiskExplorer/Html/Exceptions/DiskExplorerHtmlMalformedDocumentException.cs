using System;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Html.Exceptions;

public sealed class DiskExplorerHtmlMalformedDocumentException(string reason, Exception? inner = null)
    : InvalidOperationException($"Disk Explorer HTML report is malformed: {reason}", inner)
{
}
