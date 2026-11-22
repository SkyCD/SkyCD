using System;
using System.Globalization;

namespace SkyCD.Plugin.FileFormat.CDCatalogExpert.Exceptions;

/// <summary>
/// Raised when a catalog value cannot be encoded as Windows-1252 and would
/// otherwise be silently replaced.
/// </summary>
public sealed class ZdcCharacterNotRepresentableException(string fieldName, char character)
    : InvalidOperationException(string.Format(
        CultureInfo.InvariantCulture,
        "CD Catalog Expert field '{0}' contains character U+{1:X4}, which cannot be represented in Windows-1252.",
        fieldName,
        (int)character))
{
    /// <summary>Field that contains the character.</summary>
    public string FieldName { get; } = fieldName;

    /// <summary>Character that cannot be encoded.</summary>
    public char Character { get; } = character;
}
