using System;

namespace SkyCD.Plugin.DiskCatalogMaker.Exceptions;

/// <summary>
/// Thrown when a DiskCatalogMaker text export cannot be decoded with any supported encoding profile.
/// </summary>
public sealed class DiskCatalogMakerUnsupportedEncodingException()
    : InvalidOperationException("DiskCatalogMaker text-export encoding is not supported. Supported profiles are UTF-8 (with or without BOM), UTF-16 LE/BE and Windows-1252. The file was not decoded with a lossy fallback, so no data was silently altered.");
