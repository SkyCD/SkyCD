using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SkyCD.Plugin.Daa.Models;

internal sealed class DaaVolumeNaming
{
    private readonly string directory;
    private readonly string stem;
    private readonly int width;
    private readonly int firstNumber;

    private DaaVolumeNaming(string directory, string stem, int width, int firstNumber)
    {
        this.directory = directory;
        this.stem = stem;
        this.width = width;
        this.firstNumber = firstNumber;
    }

    public static DaaVolumeNaming? Create(string firstPath)
    {
        var directory = Path.GetDirectoryName(firstPath) ?? string.Empty;
        var name = Path.GetFileName(firstPath);
        if (!name.EndsWith(".daa", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var withoutExtension = name[..^4];
        var partIndex = withoutExtension.LastIndexOf(".part", StringComparison.OrdinalIgnoreCase);
        if (partIndex >= 0)
        {
            var digits = withoutExtension[(partIndex + 5)..];
            if ((digits.Length == 2 || digits.Length == 3) &&
                digits.All(char.IsAsciiDigit) &&
                int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                return new DaaVolumeNaming(directory, withoutExtension[..(partIndex + 5)], digits.Length, number);
            }
        }

        return new DaaVolumeNaming(directory, withoutExtension, 0, 0);
    }

    public string GetPath(int partIndex)
    {
        var name = width > 0
            ? $"{stem}{(firstNumber + partIndex).ToString($"D{width}", CultureInfo.InvariantCulture)}.daa"
            : $"{stem}.d{partIndex.ToString("D2", CultureInfo.InvariantCulture)}";
        return Path.Combine(directory, name);
    }
}
