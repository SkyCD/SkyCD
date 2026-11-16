using System;

namespace SkyCD.Plugin.CdCollect.Exceptions;

public sealed class CdCollectSqlite2VersionException(string version)
    : NotSupportedException($"Legacy CDCollect SQLite 2 schema '{version}' is not supported. Supported layouts are unversioned/version-0 files and version-4 files_v1. Use CDCollect's documented SQLite 2.8-to-3 migration and schema upgrade, then import cds3.db.");
