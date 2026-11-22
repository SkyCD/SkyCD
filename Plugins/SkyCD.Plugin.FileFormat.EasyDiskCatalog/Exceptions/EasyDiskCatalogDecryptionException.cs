using System;

namespace SkyCD.Plugin.FileFormat.EasyDiskCatalog.Exceptions;

public sealed class EasyDiskCatalogDecryptionException(string? reason = null)
    : InvalidOperationException(
        string.IsNullOrWhiteSpace(reason)
            ? "Easy Disk Catalog database cannot be decoded with the compatibility settings. The file may use an unknown password, an unsupported page size, or it is corrupt."
            : $"Easy Disk Catalog database cannot be decoded with the compatibility settings: {reason}")
{
}
