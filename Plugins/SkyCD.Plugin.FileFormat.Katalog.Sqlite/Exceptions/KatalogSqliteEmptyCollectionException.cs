using System;

namespace SkyCD.Plugin.FileFormat.Katalog.Sqlite.Exceptions;

public sealed class KatalogSqliteEmptyCollectionException()
    : InvalidOperationException("Katalog SQLite database contains no catalogs to import.")
{
}
