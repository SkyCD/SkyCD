using System;

namespace SkyCD.Plugin.Vvv.Exceptions;

public sealed class VvvMalformedException(string reason)
    : InvalidOperationException($"VVV export is malformed: {reason}")
{
}
