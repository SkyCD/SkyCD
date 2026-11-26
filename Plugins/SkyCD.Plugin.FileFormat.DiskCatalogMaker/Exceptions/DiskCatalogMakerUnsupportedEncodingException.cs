using System;

namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker.Exceptions;

/// <summary>
/// Thrown when a DiskCatalogMaker text export cannot be decoded with any supported encoding profile.
/// </summary>
public sealed class DiskCatalogMakerUnsupportedEncodingException()
    : InvalidOperationException("DiskCatalogMaker text-export encoding could not be identified or decoded strictly. Save or convert the export to UTF-8 and retry; the file was not imported.");
