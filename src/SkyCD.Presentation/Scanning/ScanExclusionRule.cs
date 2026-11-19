using System.Collections.Generic;
using System.IO.Enumeration;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// A single mask rule used to filter catalog scan entries. Exclusion rules (<see cref="IsInclude"/>
/// is false) skip matching entries; inclusion rules act as a whitelist when any are present.
/// </summary>
public sealed record ScanExclusionRule(string Pattern, ScanEntryType EntryType, bool IsInclude)
{
    public bool Matches(string name, bool isDirectory, bool ignoreCase)
    {
        if (string.IsNullOrWhiteSpace(Pattern) || string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (EntryType == ScanEntryType.File && isDirectory)
        {
            return false;
        }

        if (EntryType == ScanEntryType.Folder && !isDirectory)
        {
            return false;
        }

        return FileSystemName.MatchesSimpleExpression(Pattern.Trim(), name, ignoreCase);
    }

    public static bool IsExcludedByAny(
        IReadOnlyList<ScanExclusionRule> rules,
        string name,
        bool isDirectory,
        bool ignoreCase)
    {
        foreach (var rule in rules)
        {
            if (!rule.IsInclude && rule.Matches(name, isDirectory, ignoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whitelist check: with no inclusion rules every entry passes; otherwise an entry is accepted
    /// only when it matches at least one inclusion rule for its entry kind.
    /// </summary>
    public static bool PassesInclusion(
        IReadOnlyList<ScanExclusionRule> inclusionRules,
        string name,
        bool isDirectory,
        bool ignoreCase)
    {
        if (inclusionRules.Count == 0)
        {
            return true;
        }

        foreach (var rule in inclusionRules)
        {
            if (rule.IsInclude && rule.Matches(name, isDirectory, ignoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
