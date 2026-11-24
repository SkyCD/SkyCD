using System;
using System.Collections.Generic;
using System.Globalization;
using SkyCD.Documents;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// Culture-aware name comparer that compares digit runs by their numeric value, matching the ordering
/// users expect from Windows Explorer.
/// </summary>
internal sealed class NaturalNameComparer : IComparer<CatalogDocument>
{
    public static readonly NaturalNameComparer Instance = new();

    public int Compare(CatalogDocument? x, CatalogDocument? y) => CompareStrings(x?.DisplayName, y?.DisplayName);

    public static int CompareStrings(string? left, string? right)
    {
        left ??= string.Empty;
        right ??= string.Empty;
        var leftIndex = 0;
        var rightIndex = 0;
        while (leftIndex < left.Length && rightIndex < right.Length)
        {
            if (char.IsAsciiDigit(left[leftIndex]) && char.IsAsciiDigit(right[rightIndex]))
            {
                var leftStart = leftIndex;
                while (leftIndex < left.Length && char.IsAsciiDigit(left[leftIndex]))
                {
                    leftIndex++;
                }

                var rightStart = rightIndex;
                while (rightIndex < right.Length && char.IsAsciiDigit(right[rightIndex]))
                {
                    rightIndex++;
                }

                var digits = CompareDigitRuns(
                    left.AsSpan(leftStart, leftIndex - leftStart),
                    right.AsSpan(rightStart, rightIndex - rightStart));
                if (digits != 0)
                {
                    return digits;
                }

                continue;
            }

            var leftTextStart = leftIndex;
            while (leftIndex < left.Length && !char.IsAsciiDigit(left[leftIndex]))
            {
                leftIndex++;
            }

            var rightTextStart = rightIndex;
            while (rightIndex < right.Length && !char.IsAsciiDigit(right[rightIndex]))
            {
                rightIndex++;
            }

            var text = CultureInfo.CurrentCulture.CompareInfo.Compare(
                left.AsSpan(leftTextStart, leftIndex - leftTextStart),
                right.AsSpan(rightTextStart, rightIndex - rightTextStart),
                CompareOptions.IgnoreCase);
            if (text != 0)
            {
                return text;
            }
        }

        if (leftIndex < left.Length)
        {
            return 1;
        }

        if (rightIndex < right.Length)
        {
            return -1;
        }

        return string.Compare(left, right, StringComparison.CurrentCultureIgnoreCase);
    }

    private static int CompareDigitRuns(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        var leftDigits = left.TrimStart('0');
        var rightDigits = right.TrimStart('0');
        if (leftDigits.Length != rightDigits.Length)
        {
            return leftDigits.Length.CompareTo(rightDigits.Length);
        }

        var digits = leftDigits.SequenceCompareTo(rightDigits);
        if (digits != 0)
        {
            return digits;
        }

        // Same numeric value ("1" and "01"): the run without leading zeros goes first.
        return left.Length.CompareTo(right.Length);
    }
}
