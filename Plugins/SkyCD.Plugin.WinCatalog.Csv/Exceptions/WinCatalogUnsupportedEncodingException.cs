using System;

namespace SkyCD.Plugin.WinCatalog.Csv.Exceptions;

/// <summary>
/// Thrown when a WinCatalog CSV report cannot be decoded with any supported encoding profile.
/// </summary>
public sealed class WinCatalogUnsupportedEncodingException()
    : InvalidOperationException("WinCatalog CSV report encoding is not supported. Supported profiles are UTF-8 (with or without BOM), UTF-16 LE/BE and Windows-1252. The file was not decoded with a lossy fallback, so no data was silently altered.");
