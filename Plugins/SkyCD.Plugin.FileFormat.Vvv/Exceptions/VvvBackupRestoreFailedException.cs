using System;

namespace SkyCD.Plugin.FileFormat.Vvv.Exceptions;

public sealed class VvvBackupRestoreFailedException(string reason)
    : InvalidOperationException(
        $"VVV backup restore failed. The file is not a valid Firebird logical backup, is truncated or corrupted, or uses an unsupported encryption or page format. Details: {reason}")
{
}
