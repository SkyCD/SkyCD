using System;

namespace SkyCD.Plugin.FileFormat.AbeMeda.Text.Exceptions;

/// <summary>
/// Thrown when a NeoFinder text export uses an unsupported character encoding.
/// </summary>
public sealed class NeoFinderTextUnsupportedEncodingException()
    : InvalidOperationException("NeoFinder text export encoding could not be detected. UTF-8, UTF-16 and the common Windows code pages are supported. The export was not decoded with a lossy fallback, so no data was silently altered. Export as UTF-8, or convert the file before importing.");
