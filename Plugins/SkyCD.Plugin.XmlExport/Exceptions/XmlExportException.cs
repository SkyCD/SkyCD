using System;

namespace SkyCD.Plugin.XmlExport.Exceptions;

/// <summary>
/// Base failure for the SkyCD XML catalog and list exporters.
/// </summary>
public class XmlExportException(string? reason = null)
    : InvalidOperationException(
        string.IsNullOrWhiteSpace(reason)
            ? "SkyCD XML export failed."
            : $"SkyCD XML export failed: {reason}")
{
}
