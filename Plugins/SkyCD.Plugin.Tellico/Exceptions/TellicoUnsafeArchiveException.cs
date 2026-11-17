using System;

namespace SkyCD.Plugin.Tellico.Exceptions;

public sealed class TellicoUnsafeArchiveException(string reason)
    : InvalidOperationException($"Tellico collection archive is unsafe and was not opened: {reason}")
{
}
