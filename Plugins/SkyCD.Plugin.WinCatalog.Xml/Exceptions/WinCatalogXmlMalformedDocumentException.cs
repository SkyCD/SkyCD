using System;

namespace SkyCD.Plugin.WinCatalog.Xml.Exceptions;

/// <summary>
/// Thrown when a WinCatalog XML report is malformed or exceeds the bounded parsing resources.
/// </summary>
public sealed class WinCatalogXmlMalformedDocumentException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
