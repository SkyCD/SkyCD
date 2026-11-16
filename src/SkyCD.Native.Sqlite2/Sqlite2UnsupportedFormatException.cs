using System;

namespace SkyCD.Native.Sqlite2;

public sealed class Sqlite2UnsupportedFormatException(uint format) : NotSupportedException($"Unsupported SQLite 2 storage format {format}.")
{
    public uint Format { get; } = format;
}
