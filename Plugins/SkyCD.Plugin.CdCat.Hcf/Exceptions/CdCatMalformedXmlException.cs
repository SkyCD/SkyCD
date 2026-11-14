using System;

namespace SkyCD.Plugin.CdCat.Hcf.Exceptions;

/// <summary>
/// Thrown when CdCat XML structure contains invalid or unparseable XML syntax.
/// </summary>
public sealed class CdCatMalformedXmlException(string? details = null)
    : InvalidOperationException(
        string.IsNullOrWhiteSpace(details)
            ? "CdCat catalog contains malformed XML content."
            : $"CdCat catalog contains malformed XML content: {details}");
