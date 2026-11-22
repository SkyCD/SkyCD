using System;

namespace SkyCD.Plugin.FileFormat.GCstar.Exceptions;

public sealed class GCstarMalformedDocumentException(string reason, Exception? inner = null)
    : InvalidOperationException($"GCstar collection export is malformed: {reason}", inner)
{
}
