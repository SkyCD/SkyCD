using System;

namespace SkyCD.Plugin.FileFormat.CDCollect.Exceptions;

public sealed class CdCollectSqlite2CorruptException(string reason)
    : InvalidOperationException($"Legacy CDCollect SQLite 2 catalog is corrupt or truncated: {reason}. Restore an intact cds.db backup.");
