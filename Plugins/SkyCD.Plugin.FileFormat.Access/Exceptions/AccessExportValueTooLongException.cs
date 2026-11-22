using System;

namespace SkyCD.Plugin.FileFormat.Access.Exceptions;

public sealed class AccessExportValueTooLongException(string column) : InvalidOperationException(
    $"Access TEXT column '{column}' exceeds its 255-character limit. Select a shorter value before exporting.");
