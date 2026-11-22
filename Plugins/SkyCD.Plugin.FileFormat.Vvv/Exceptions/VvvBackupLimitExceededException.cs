using System;

namespace SkyCD.Plugin.FileFormat.Vvv.Exceptions;

public sealed class VvvBackupLimitExceededException(string limit)
    : InvalidOperationException($"VVV backup exceeds the import limit: {limit}.")
{
}
