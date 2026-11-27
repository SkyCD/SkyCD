using System;
using System.Collections.Generic;
using SkyCD.SDK.Capabilities.FileFormats;

namespace SkyCD.UI.Localization;

/// <summary>
/// Renders loss-assessment items as localized, user-facing dialog lines.
/// </summary>
public static class CatalogDataLossText
{
    public static IReadOnlyList<string> BuildLines(IReadOnlyList<FileFormatDataLossItem> items)
    {
        var localization = LocalizationService.Current;
        var lines = new List<string>(items.Count);
        foreach (var item in items)
        {
            var category = DescribeCategory(item.Category);
            var name = DescribeName(item.Category, item.Name);
            lines.Add(string.IsNullOrWhiteSpace(item.Sample)
                ? localization.Format(UiKeys.DataLossItemLine, category, name, item.AffectedCount)
                : localization.Format(UiKeys.DataLossItemLineWithSample, category, name, item.AffectedCount,
                    item.Sample));
        }

        return lines;
    }

    private static string DescribeCategory(string category) => category switch
    {
        FileFormatDataLossCategories.Entries => LocalizationService.Current.Get(UiKeys.DataLossCategoryEntries),
        FileFormatDataLossCategories.Collections => LocalizationService.Current.Get(UiKeys.DataLossCategoryCollections),
        FileFormatDataLossCategories.Statuses => LocalizationService.Current.Get(UiKeys.DataLossCategoryStatuses),
        FileFormatDataLossCategories.Loans => LocalizationService.Current.Get(UiKeys.DataLossCategoryLoans),
        FileFormatDataLossCategories.Borrowers => LocalizationService.Current.Get(UiKeys.DataLossCategoryBorrowers),
        FileFormatDataLossCategories.ScanExclusions =>
            LocalizationService.Current.Get(UiKeys.DataLossCategoryScanExclusions),
        FileFormatDataLossCategories.AiScanExclusions =>
            LocalizationService.Current.Get(UiKeys.DataLossCategoryAiScanExclusions),
        _ => category
    };

    private static string DescribeName(string category, string name)
    {
        if (category == FileFormatDataLossCategories.Entries)
        {
            return DescribeEntryField(name);
        }

        var localization = LocalizationService.Current;
        return name switch
        {
            "virtualCollections" => localization.Get(UiKeys.DataLossVirtualCollections),
            "statusDefinitions" => localization.Get(UiKeys.DataLossStatusDefinitions),
            "customRules" => localization.Get(UiKeys.DataLossScanRules),
            "customMimeTypes" => localization.Get(UiKeys.DataLossAiExclusions),
            "activeLoans" => localization.Get(UiKeys.DataLossActiveLoans),
            "contacts" => localization.Get(UiKeys.DataLossContacts),
            _ => name
        };
    }

    private static string DescribeEntryField(string name)
    {
        var localization = LocalizationService.Current;
        if (name.StartsWith(FileFormatDataLossFields.PropertyKeyPrefix, StringComparison.Ordinal))
        {
            return localization.Format(UiKeys.DataLossFieldProperty,
                name[FileFormatDataLossFields.PropertyKeyPrefix.Length..]);
        }

        return name switch
        {
            FileFormatDataLossFields.Id => localization.Get(UiKeys.DataLossFieldId),
            FileFormatDataLossFields.ParentId => localization.Get(UiKeys.DataLossFieldParentId),
            FileFormatDataLossFields.Kind => localization.Get(UiKeys.DataLossFieldKind),
            FileFormatDataLossFields.Size => localization.Get(UiKeys.DataLossFieldSize),
            FileFormatDataLossFields.Status => localization.Get(UiKeys.DataLossFieldStatus),
            FileFormatDataLossFields.Properties => localization.Get(UiKeys.DataLossFieldProperties),
            _ => name
        };
    }
}
