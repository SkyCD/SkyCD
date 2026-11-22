using System;

namespace SkyCD.Plugin.FileFormat.Vvv.Exceptions;

public sealed class VvvMalformedException(string reason)
    : InvalidOperationException($"VVV export is malformed: {reason}")
{
}
