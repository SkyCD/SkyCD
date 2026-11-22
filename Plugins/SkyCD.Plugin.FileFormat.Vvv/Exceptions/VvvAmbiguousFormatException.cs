using System;

namespace SkyCD.Plugin.FileFormat.Vvv.Exceptions;

public sealed class VvvAmbiguousFormatException(string reason)
    : InvalidOperationException($"VVV export format is ambiguous: {reason}")
{
}
