using System;

namespace SkyCD.Plugin.FileFormat.EasyDiskCatalog.Exceptions;

public sealed class EasyDiskCatalogCorruptException(string reason)
    : InvalidOperationException(
        $"Easy Disk Catalog database is corrupt or truncated: {reason}")
{
}
