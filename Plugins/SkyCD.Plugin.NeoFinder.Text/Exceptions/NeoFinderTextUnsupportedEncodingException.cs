using System;

namespace SkyCD.Plugin.NeoFinder.Text.Exceptions;

/// <summary>
/// Thrown when a NeoFinder text export uses an unsupported character encoding.
/// </summary>
public sealed class NeoFinderTextUnsupportedEncodingException()
    : InvalidOperationException("NeoFinder text export uses an unsupported character encoding. Export as UTF-8 or UTF-16, or convert the file before importing.");
