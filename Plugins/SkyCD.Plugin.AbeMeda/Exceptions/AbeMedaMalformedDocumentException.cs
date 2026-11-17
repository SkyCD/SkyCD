using System;

namespace SkyCD.Plugin.AbeMeda.Exceptions;

public sealed class AbeMedaMalformedDocumentException(string reason, Exception? inner = null)
    : InvalidOperationException($"abeMeda XML export is malformed: {reason}", inner)
{
}
