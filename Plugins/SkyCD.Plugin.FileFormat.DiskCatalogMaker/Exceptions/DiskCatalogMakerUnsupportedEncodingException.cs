using System;

namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker.Exceptions;

/// <summary>
/// Thrown when a DiskCatalogMaker text export cannot be decoded with any supported encoding profile.
/// </summary>
public sealed class DiskCatalogMakerUnsupportedEncodingException()
    : InvalidOperationException("DiskCatalogMaker text-export encoding could not be detected. UTF-8, UTF-16 and the common Windows code pages are supported. The export was not decoded with a lossy fallback, so no data was silently altered. Re-save the export as UTF-8 and import it again.");
