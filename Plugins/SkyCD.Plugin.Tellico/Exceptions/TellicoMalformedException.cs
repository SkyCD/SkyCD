using System;

namespace SkyCD.Plugin.Tellico.Exceptions;

public sealed class TellicoMalformedException(string reason)
    : InvalidOperationException($"Tellico collection is malformed: {reason}")
{
}
