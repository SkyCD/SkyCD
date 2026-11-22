using System;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Exceptions;

/// <summary>Raised when a text field cannot be represented in its declared encoding.</summary>
public sealed class DedTextEncodingException(string field)
    : InvalidOperationException(
        $"Disk Explorer database text field '{field}' cannot be represented in the required encoding.");
