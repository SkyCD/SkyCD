using System;

namespace SkyCD.Plugin.Vvv.Exceptions;

public sealed class VvvBackupMalformedException(string reason)
    : InvalidOperationException($"VVV backup is not importable: {reason}")
{
}
