using System;

namespace SkyCD.Plugin.FileFormat.WinCatalog.Csv.Exceptions;

/// <summary>
/// Thrown when a WinCatalog CSV report cannot be decoded with any supported encoding profile.
/// </summary>
public sealed class WinCatalogUnsupportedEncodingException()
    : InvalidOperationException("WinCatalog CSV report encoding could not be detected. UTF-8, UTF-16 and the common Windows code pages are supported. The report was not decoded with a lossy fallback, so no data was silently altered. Re-save the report as UTF-8 and import it again.");
