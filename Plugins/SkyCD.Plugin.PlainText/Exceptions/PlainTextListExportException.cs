using System;

namespace SkyCD.Plugin.PlainText.Exceptions;

public sealed class PlainTextListExportException(string? reason = null)
    : InvalidOperationException(
        string.IsNullOrWhiteSpace(reason)
            ? "Catalog plain-text list export failed."
            : $"Catalog plain-text list export failed: {reason}")
{
}
