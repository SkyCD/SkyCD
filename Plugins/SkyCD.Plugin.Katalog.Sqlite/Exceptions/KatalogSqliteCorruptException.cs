using System;

namespace SkyCD.Plugin.Katalog.Sqlite.Exceptions;

public sealed class KatalogSqliteCorruptException(string reason)
    : InvalidOperationException($"Katalog SQLite database is corrupt or unreadable: {reason}")
{
}
