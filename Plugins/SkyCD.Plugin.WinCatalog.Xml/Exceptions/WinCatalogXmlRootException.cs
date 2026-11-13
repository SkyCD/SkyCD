using System;

namespace SkyCD.Plugin.WinCatalog.Xml.Exceptions;

/// <summary>
/// Thrown when a stream does not contain recognizable WinCatalog XML report content.
/// </summary>
public sealed class WinCatalogXmlRootException()
    : InvalidOperationException("The file does not contain a recognizable WinCatalog XML report. Expected a WinCatalog export with disk, folder, or file items and catalog metadata fields.");
