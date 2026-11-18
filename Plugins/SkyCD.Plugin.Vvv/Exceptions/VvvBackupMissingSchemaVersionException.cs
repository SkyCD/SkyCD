using System;

namespace SkyCD.Plugin.Vvv.Exceptions;

public sealed class VvvBackupMissingSchemaVersionException()
    : InvalidOperationException(
        "VVV backup has no SERVICE version table. The file is truncated or corrupted, or it predates every verified schema version. Restore it with VVV and export the volumes instead.")
{
}
