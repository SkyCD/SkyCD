using System;

namespace SkyCD.Plugin.FileFormat.WhereIsIt.Exceptions;

public sealed class WhereIsItUnsupportedSchemaException(string? reason = null)
    : InvalidOperationException(
        string.IsNullOrWhiteSpace(reason)
            ? "XML document is not a Where Is It? XML report."
            : $"XML document is not a Where Is It? XML report: {reason}")
{
}
