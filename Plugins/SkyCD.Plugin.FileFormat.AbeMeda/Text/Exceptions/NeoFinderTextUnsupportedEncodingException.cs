using System;

namespace SkyCD.Plugin.FileFormat.AbeMeda.Text.Exceptions;

/// <summary>
/// Thrown when a NeoFinder text export uses an unsupported character encoding.
/// </summary>
public sealed class NeoFinderTextUnsupportedEncodingException()
    : InvalidOperationException("NeoFinder text-export encoding could not be identified or decoded strictly. Export or convert the file to UTF-8 and retry; the file was not imported.");
