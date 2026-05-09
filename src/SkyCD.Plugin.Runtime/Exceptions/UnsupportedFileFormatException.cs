using System;

namespace SkyCD.Plugin.Runtime.Exceptions;

public sealed class UnsupportedFileFormatException(string fileName)
    : InvalidOperationException($"Unsupported file format for '{fileName}'.");
