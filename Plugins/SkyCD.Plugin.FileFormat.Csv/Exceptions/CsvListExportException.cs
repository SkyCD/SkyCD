using System;

namespace SkyCD.Plugin.FileFormat.Csv.Exceptions;

public sealed class CsvListExportException(string? reason = null)
    : InvalidOperationException(
        string.IsNullOrWhiteSpace(reason)
            ? "Catalog list CSV export failed."
            : $"Catalog list CSV export failed: {reason}")
{
}
