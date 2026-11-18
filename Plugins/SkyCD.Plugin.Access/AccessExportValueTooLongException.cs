using System;

namespace SkyCD.Plugin.Access;

public sealed class AccessExportValueTooLongException(string column) : InvalidOperationException(
    $"Access TEXT column '{column}' exceeds its 255-character limit. Select a shorter value before exporting.");
