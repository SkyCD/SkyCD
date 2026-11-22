using System;

namespace SkyCD.Plugin.FileFormat.Katalog.Sqlite.Exceptions;

public sealed class KatalogSqliteCorruptException(string reason)
    : InvalidOperationException($"Katalog SQLite database is corrupt or unreadable: {reason}")
{
}
