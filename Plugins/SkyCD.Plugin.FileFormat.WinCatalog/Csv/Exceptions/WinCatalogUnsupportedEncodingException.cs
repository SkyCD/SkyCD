using System;

namespace SkyCD.Plugin.FileFormat.WinCatalog.Csv.Exceptions;

/// <summary>
/// Thrown when a WinCatalog CSV report cannot be decoded with any supported encoding profile.
/// </summary>
public sealed class WinCatalogUnsupportedEncodingException()
    : InvalidOperationException("WinCatalog CSV report encoding could not be identified or decoded strictly. Save or convert the report to UTF-8 and retry; the file was not imported.");
