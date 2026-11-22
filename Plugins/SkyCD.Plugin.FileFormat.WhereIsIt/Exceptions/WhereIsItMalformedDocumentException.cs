using System;

namespace SkyCD.Plugin.FileFormat.WhereIsIt.Exceptions;

public sealed class WhereIsItMalformedDocumentException(string reason, Exception? inner = null)
    : InvalidOperationException($"Where Is It? XML report is malformed: {reason}", inner)
{
}
