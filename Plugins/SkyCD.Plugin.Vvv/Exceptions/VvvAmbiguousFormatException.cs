using System;

namespace SkyCD.Plugin.Vvv.Exceptions;

public sealed class VvvAmbiguousFormatException(string reason)
    : InvalidOperationException($"VVV export format is ambiguous: {reason}")
{
}
