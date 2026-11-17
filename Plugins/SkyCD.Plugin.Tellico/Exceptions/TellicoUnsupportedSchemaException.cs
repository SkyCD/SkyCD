using System;

namespace SkyCD.Plugin.Tellico.Exceptions;

public sealed class TellicoUnsupportedSchemaException(string reason)
    : InvalidOperationException($"Unsupported Tellico collection schema: {reason}")
{
}
