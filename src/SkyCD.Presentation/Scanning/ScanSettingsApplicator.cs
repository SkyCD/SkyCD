using System;
using System.Collections.Generic;
using SkyCD.Documents;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Converts persisted scan settings into the traversal policy consumed by catalog scanners.
/// </summary>
public static class ScanSettingsApplicator
{
    public static ScanTraversalOptions BuildTraversalOptions(
        AppOptionsDocument options,
        IReadOnlyList<ScanExclusionRuleDocument>? rules = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<ScanExclusionRule> exclusionRules = [];
        List<ScanExclusionRule> inclusionRules = [];
        foreach (var rule in rules ?? [])
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern))
            {
                continue;
            }

            var target = rule.IsInclude ? inclusionRules : exclusionRules;
            target.Add(new ScanExclusionRule(
                rule.Pattern.Trim(),
                ParseEntryType(rule.EntryType),
                rule.IsInclude));
        }

        return new ScanTraversalOptions
        {
            LinkFollowPolicy = options.SkipSymbolicLinks
                ? LinkFollowPolicy.NeverFollowLinks
                : LinkFollowPolicy.FollowDirectoryLinksOnce,
            HiddenSystemFilePolicy = ParseHiddenSystemFilePolicy(options.HiddenSystemFilePolicy),
            ExclusionRules = exclusionRules,
            InclusionRules = inclusionRules,
            SkipRecycleBins = options.SkipRecycleBins,
            SkipWellKnownSystemFolders = options.SkipWellKnownSystemFolders,
            SkipEmptyFolders = options.SkipEmptyFolders,
            SkipOnlineOnlyCloudFiles = options.SkipOnlineOnlyCloudFiles
        };
    }

    public static ScanEntryType ParseEntryType(string? value) => value switch
    {
        "File" => ScanEntryType.File,
        "Folder" => ScanEntryType.Folder,
        _ => ScanEntryType.Both
    };

    public static string ToEntryTypeString(ScanEntryType entryType) => entryType switch
    {
        ScanEntryType.File => "File",
        ScanEntryType.Folder => "Folder",
        _ => "Both"
    };

    public static HiddenSystemFilePolicy ParseHiddenSystemFilePolicy(string? value) => value switch
    {
        "SkipHidden" => HiddenSystemFilePolicy.SkipHidden,
        "SkipSystem" => HiddenSystemFilePolicy.SkipSystem,
        "SkipHiddenAndSystem" => HiddenSystemFilePolicy.SkipHiddenAndSystem,
        _ => HiddenSystemFilePolicy.Include
    };

    public static string ToHiddenSystemFilePolicyString(HiddenSystemFilePolicy policy) => policy switch
    {
        HiddenSystemFilePolicy.SkipHidden => "SkipHidden",
        HiddenSystemFilePolicy.SkipSystem => "SkipSystem",
        HiddenSystemFilePolicy.SkipHiddenAndSystem => "SkipHiddenAndSystem",
        _ => "Include"
    };
}
