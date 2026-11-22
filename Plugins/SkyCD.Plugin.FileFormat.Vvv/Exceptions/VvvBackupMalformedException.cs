using System;

namespace SkyCD.Plugin.FileFormat.Vvv.Exceptions;

public sealed class VvvBackupMalformedException(string reason)
    : InvalidOperationException($"VVV backup is not importable: {reason}")
{
}
