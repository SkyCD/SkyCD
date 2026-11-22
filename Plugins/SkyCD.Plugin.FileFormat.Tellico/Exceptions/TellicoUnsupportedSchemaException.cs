using System;

namespace SkyCD.Plugin.FileFormat.Tellico.Exceptions;

public sealed class TellicoUnsupportedSchemaException(string reason)
    : InvalidOperationException($"Unsupported Tellico collection schema: {reason}")
{
}
