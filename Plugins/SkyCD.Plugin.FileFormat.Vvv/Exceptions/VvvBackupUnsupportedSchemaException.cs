using System;

namespace SkyCD.Plugin.FileFormat.Vvv.Exceptions;

public sealed class VvvBackupUnsupportedSchemaException(int schemaVersion)
    : InvalidOperationException(
        $"VVV backup schema version {schemaVersion} is not supported. Supported versions are 14 through 23.")
{
}
