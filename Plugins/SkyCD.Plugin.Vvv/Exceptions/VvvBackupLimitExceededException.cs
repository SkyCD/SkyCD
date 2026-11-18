using System;

namespace SkyCD.Plugin.Vvv.Exceptions;

public sealed class VvvBackupLimitExceededException(string limit)
    : InvalidOperationException($"VVV backup exceeds the import limit: {limit}.")
{
}
