using System;

namespace SkyCD.Native.Sqlite2;

public sealed class Sqlite2Exception(string reason) : InvalidOperationException($"SQLite 2 operation failed: {reason}");
