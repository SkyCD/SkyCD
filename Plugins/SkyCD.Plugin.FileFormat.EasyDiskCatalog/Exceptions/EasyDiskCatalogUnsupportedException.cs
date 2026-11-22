using System;

namespace SkyCD.Plugin.FileFormat.EasyDiskCatalog.Exceptions;

public sealed class EasyDiskCatalogUnsupportedException(string? reason = null)
    : InvalidOperationException(
        string.IsNullOrWhiteSpace(reason)
            ? "File is not an Easy Disk Catalog (.edcdb) database."
            : $"File is not an Easy Disk Catalog (.edcdb) database: {reason}")
{
}
