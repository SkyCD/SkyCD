using System;

namespace SkyCD.Plugin.FileFormat.Katalog.Sqlite.Exceptions;

public sealed class KatalogSqliteUnsupportedSchemaException(string reason)
    : InvalidOperationException($"Unsupported Katalog database schema: {reason}")
{
}
