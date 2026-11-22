using System;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Html.Exceptions;

public sealed class DiskExplorerHtmlUnsupportedSchemaException(string? reason = null)
    : InvalidOperationException(
        string.IsNullOrWhiteSpace(reason)
            ? "HTML document is not a Disk Explorer HTML report."
            : $"HTML document is not a Disk Explorer HTML report: {reason}")
{
}
