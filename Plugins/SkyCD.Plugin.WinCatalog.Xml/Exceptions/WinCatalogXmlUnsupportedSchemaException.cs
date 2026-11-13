using System;

namespace SkyCD.Plugin.WinCatalog.Xml.Exceptions;

/// <summary>
/// Thrown when a WinCatalog XML report declares a schema version this plugin does not support.
/// </summary>
public sealed class WinCatalogXmlUnsupportedSchemaException(string version)
    : InvalidOperationException($"WinCatalog XML report declares unsupported schema version '{version}'. This plugin supports reports exported by WinCatalog 2019 and newer (or generic 'x.y' schema versions).");
