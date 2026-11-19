using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SkyCD.Formatting;

/// <summary>
/// Renders Windows file-attribute bitmasks as readable flag names.
/// Uses an explicit bit-to-name table because <see cref="System.IO.FileAttributes"/>
/// does not define the Windows-only flags (Pinned, Unpinned, recall bits, strictly
/// sequential): when any undefined bit is set, <c>Enum.ToString()</c> returns the
/// plain decimal number for the whole mask.
/// </summary>
public static class FileAttributesFormatting
{
    private static readonly (uint Mask, string LocalizationKey, string EnglishName)[] Flags =
    [
        (0x00000001, "AttributeReadOnly", "Read-only"),
        (0x00000002, "AttributeHidden", "Hidden"),
        (0x00000004, "AttributeSystem", "System"),
        (0x00000010, "AttributeDirectory", "Directory"),
        (0x00000020, "AttributeArchive", "Archive"),
        (0x00000040, "AttributeDevice", "Device"),
        (0x00000080, "AttributeNormal", "Normal"),
        (0x00000100, "AttributeTemporary", "Temporary"),
        (0x00000200, "AttributeSparseFile", "Sparse file"),
        (0x00000400, "AttributeReparsePoint", "Reparse point"),
        (0x00000800, "AttributeCompressed", "Compressed"),
        (0x00001000, "AttributeOffline", "Offline"),
        (0x00002000, "AttributeNotContentIndexed", "Not content indexed"),
        (0x00004000, "AttributeEncrypted", "Encrypted"),
        (0x00008000, "AttributeIntegrityStream", "Integrity stream"),
        (0x00020000, "AttributeNoScrubData", "No scrub data"),
        (0x00040000, "AttributeRecallOnOpen", "Recall on open"),
        (0x00080000, "AttributePinned", "Pinned"),
        (0x00100000, "AttributeUnpinned", "Unpinned"),
        (0x00200000, "AttributeStrictlySequential", "Strictly sequential"),
        (0x00400000, "AttributeRecallOnDataAccess", "Recall on data access"),
    ];

    /// <summary>
    /// Reads an attribute bitmask from a stored property value: any integral CLR
    /// value, a <see cref="System.IO.FileAttributes"/> enum value, or a decimal string.
    /// Readable text (for example "ReadOnly, Archive") is not a mask and returns false.
    /// </summary>
    public static bool TryGetMask(object? value, out uint mask)
    {
        switch (value)
        {
            case null:
                mask = 0;
                return false;
            case uint parsed:
                mask = parsed;
                return true;
            case int integer:
                mask = unchecked((uint)integer);
                return true;
            case ulong wide:
                mask = unchecked((uint)wide);
                return true;
            case long big:
                mask = unchecked((uint)big);
                return true;
            case ushort shortMask:
                mask = shortMask;
                return true;
            case short shortValue:
                mask = unchecked((uint)shortValue);
                return true;
            case byte byteMask:
                mask = byteMask;
                return true;
            case sbyte signedByte:
                mask = unchecked((uint)signedByte);
                return true;
            case Enum attributes:
                mask = unchecked((uint)Convert.ToInt64(attributes, CultureInfo.InvariantCulture));
                return true;
            case string text:
                var trimmed = text.Trim();
                if (trimmed.Length > 0 &&
                    trimmed.All(char.IsAsciiDigit) &&
                    ulong.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var digits))
                {
                    mask = unchecked((uint)digits);
                    return true;
                }

                mask = 0;
                return false;
            default:
                mask = 0;
                return false;
        }
    }

    /// <summary>
    /// Formats a mask as a comma-separated list of flag names in ascending bit order.
    /// <paramref name="resolveName"/> maps a localization key (for example
    /// "AttributeArchive") to localized text, or returns null to keep the English name.
    /// A zero mask renders as the localized "None" name; undefined leftover bits are
    /// appended as a hex remainder so no information is silently lost.
    /// </summary>
    public static string FormatFileAttributes(uint mask, Func<string, string?> resolveName)
    {
        ArgumentNullException.ThrowIfNull(resolveName);

        if (mask == 0)
        {
            return resolveName("AttributeNone") ?? "None";
        }

        var names = new List<string>();
        var remainder = mask;
        foreach (var (bit, localizationKey, englishName) in Flags)
        {
            if ((mask & bit) != 0)
            {
                names.Add(resolveName(localizationKey) ?? englishName);
                remainder &= ~bit;
            }
        }

        if (remainder != 0)
        {
            names.Add("0x" + remainder.ToString("X", CultureInfo.InvariantCulture));
        }

        return string.Join(", ", names);
    }
}
