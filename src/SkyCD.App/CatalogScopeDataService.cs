using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Documents;
using SkyCD.SDK.Capabilities.Catalog;
using SkyCD.SDK.Capabilities.FileFormats;

namespace SkyCD.App;

/// <summary>
/// Collects catalog-scope data present in the open catalog that file formats may not be able to store.
/// Defaults that the host recreates on open are excluded so only real data is reported.
/// </summary>
public static class CatalogScopeDataService
{
    private const int SampleSize = 3;

    public static IReadOnlyList<FileFormatDataLossItem> Collect(
        IReadOnlyList<VirtualCollectionDocument> collections,
        IReadOnlyList<StatusVariantDocument> statuses,
        IReadOnlyList<ScanExclusionRuleDocument> scanRules,
        IReadOnlyList<AiScanExclusionDocument> aiExclusions,
        IEnumerable<ICatalogAuxiliaryDataProvider> auxiliaryProviders)
    {
        var items = new List<FileFormatDataLossItem>();

        if (collections.Count > 0)
        {
            items.Add(new FileFormatDataLossItem(
                FileFormatDataLossCategories.Collections,
                "virtualCollections",
                collections.Count,
                Sample(collections.Select(static collection => collection.Name))));
        }

        var defaultStatusIds = new HashSet<string>(
            new SkyCD.Documents.Repository.StatusVariantDocumentRepository().CreateDefaultEntries()
                .Select(static status => status.Id),
            StringComparer.OrdinalIgnoreCase);
        var customStatuses = statuses.Where(status => !defaultStatusIds.Contains(status.Id)).ToList();
        if (customStatuses.Count > 0)
        {
            items.Add(new FileFormatDataLossItem(
                FileFormatDataLossCategories.Statuses,
                "statusDefinitions",
                customStatuses.Count,
                Sample(customStatuses.Select(static status => status.Name))));
        }

        var defaultRules = new HashSet<string>(
            WellKnownScanExclusions.GetDefaultExclusionRules().Select(DescribeRule),
            StringComparer.OrdinalIgnoreCase);
        var customRules = scanRules.Where(rule => !defaultRules.Contains(DescribeRule(rule))).ToList();
        if (customRules.Count > 0)
        {
            items.Add(new FileFormatDataLossItem(
                FileFormatDataLossCategories.ScanExclusions,
                "customRules",
                customRules.Count,
                Sample(customRules.Select(static rule => rule.Pattern))));
        }

        var defaultMimeTypes = new HashSet<string>(
            WellKnownAiScanExclusions.GetDefaultExcludedMimeTypes(),
            StringComparer.OrdinalIgnoreCase);
        var customMimeTypes = aiExclusions
            .Where(exclusion => !defaultMimeTypes.Contains(exclusion.MimeType))
            .ToList();
        if (customMimeTypes.Count > 0)
        {
            items.Add(new FileFormatDataLossItem(
                FileFormatDataLossCategories.AiScanExclusions,
                "customMimeTypes",
                customMimeTypes.Count,
                Sample(customMimeTypes.Select(static exclusion => exclusion.MimeType))));
        }

        foreach (var provider in auxiliaryProviders)
        {
            IReadOnlyList<CatalogAuxiliaryDataGroup> groups;
            try
            {
                groups = provider.GetAuxiliaryData();
            }
            catch
            {
                continue;
            }

            foreach (var group in groups)
            {
                if (group.AffectedCount > 0)
                {
                    items.Add(new FileFormatDataLossItem(group.Category, group.Name, group.AffectedCount, group.Sample));
                }
            }
        }

        return items;
    }

    private static string DescribeRule(ScanExclusionRuleDocument rule) =>
        $"{rule.Pattern}\u001f{rule.EntryType}\u001f{rule.IsInclude}";

    private static string? Sample(IEnumerable<string?> names)
    {
        var sample = names
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Take(SampleSize)
            .ToList();
        return sample.Count == 0 ? null : string.Join(", ", sample);
    }
}
