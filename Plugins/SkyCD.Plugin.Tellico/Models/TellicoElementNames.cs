using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Tellico.Models;

/// <summary>
/// Name vocabulary for Tellico collection documents. Element names follow the
/// published Tellico DTD (v11; syntax version 12 adds filter-rule values only).
/// Attribute matching is exact first with a case-insensitive fallback.
/// </summary>
public static class TellicoElementNames
{
    public const string RootElement = "tellico";
    public const string SyntaxVersionAttribute = "syntaxVersion";
    public const string TellicoNamespace = "http://periapsis.org/tellico/";

    public const string CollectionElement = "collection";
    public const string FieldsElement = "fields";
    public const string FieldElement = "field";
    public const string PropertyElement = "prop";
    public const string EntryElement = "entry";
    public const string ImagesElement = "images";
    public const string ImageElement = "image";
    public const string BorrowersElement = "borrowers";
    public const string BorrowerElement = "borrower";
    public const string LoanElement = "loan";
    public const string IdElement = "id";

    public const int CurrentSyntaxVersion = 12;
    public const int MinimumSupportedSyntaxVersion = 4;

    /// <summary>
    /// Field types from the Tellico field definition (field.h). Deprecated
    /// Table2/Dependent values are read as plain text.
    /// </summary>
    public enum TellicoFieldType
    {
        Unknown = 0,
        Line = 1,
        Para = 2,
        Choice = 3,
        Bool = 4,
        Number = 6,
        Url = 7,
        Table = 8,
        Image = 10,
        Date = 12,
        Rating = 14
    }

    public static TellicoFieldType ParseFieldType(string? value)
    {
        if (int.TryParse(value?.Trim(), out var number) && Enum.IsDefined(typeof(TellicoFieldType), number))
        {
            return (TellicoFieldType)number;
        }

        return TellicoFieldType.Unknown;
    }

    [Flags]
    public enum TellicoFieldFlags
    {
        None = 0,
        AllowMultiple = 1
    }

    public static bool AllowsMultiple(string? flags)
    {
        if (!int.TryParse(flags?.Trim(), out var number))
        {
            return false;
        }

        return (number & (int)TellicoFieldFlags.AllowMultiple) != 0;
    }

    private static readonly HashSet<string> ImageFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "cover", "front", "back", "image", "picture", "photo", "obverse", "reverse"
    };

    public static bool IsImageField(string name)
    {
        if (ImageFields.Contains(name))
        {
            return true;
        }

        var lower = name.ToLowerInvariant();
        return lower.EndsWith("image", StringComparison.Ordinal)
            || lower.EndsWith("cover", StringComparison.Ordinal)
            || lower.EndsWith("photo", StringComparison.Ordinal);
    }
}
