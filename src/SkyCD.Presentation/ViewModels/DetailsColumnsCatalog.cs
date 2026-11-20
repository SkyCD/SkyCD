using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using SkyCD.Documents;
using SkyCD.UI.Controls.Lists;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public static class DetailsColumnsCatalog
{
    public const string NameId = "name";
    public const string SizeId = "size";
    public const string TypeId = "type";
    public const string StatusId = "status";
    public const string LentToId = "lentTo";
    public const string LocationId = "location";

    public static IReadOnlyList<DetailsColumnDefinition> All { get; } =
    [
        new()
        {
            Id = NameId,
            HeaderKey = TextKeys.Name2,
            ValuePath = nameof(CatalogDocument.DisplayName),
            Width = new GridLength(500, GridUnitType.Pixel),
            IsRequired = true
        },
        new()
        {
            Id = SizeId,
            HeaderKey = TextKeys.Size,
            ValuePath = "DisplaySize",
            Width = new GridLength(100, GridUnitType.Pixel),
            HeaderAlignment = HorizontalAlignment.Right,
            ValueAlignment = HorizontalAlignment.Right
        },
        new()
        {
            Id = TypeId,
            HeaderKey = TextKeys.Type2,
            ValuePath = "DisplayType",
            Width = new GridLength(120, GridUnitType.Pixel),
            IsVisibleByDefault = false
        },
        new()
        {
            Id = StatusId,
            HeaderKey = TextKeys.Status,
            ValuePath = nameof(CatalogDocument.Status),
            Width = new GridLength(120, GridUnitType.Pixel),
            IsVisibleByDefault = false
        },
        new()
        {
            Id = LentToId,
            HeaderKey = UiKeys.LentTo,
            ValuePath = nameof(CatalogDocument.LentTo),
            Width = new GridLength(140, GridUnitType.Pixel)
        },
        new()
        {
            Id = LocationId,
            HeaderKey = UiKeys.Location,
            ValuePath = nameof(CatalogDocument.Location),
            Width = new GridLength(140, GridUnitType.Pixel)
        }
    ];

    public static DetailsColumnDefinition? Find(string? id)
    {
        return string.IsNullOrWhiteSpace(id)
            ? null
            : All.FirstOrDefault(definition => string.Equals(definition.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public static List<DetailsColumnSettingDocument> CreateDefaultSettings()
    {
        return All
            .Select(definition => new DetailsColumnSettingDocument
            {
                Id = definition.Id,
                IsVisible = definition.IsRequired || definition.IsVisibleByDefault
            })
            .ToList();
    }

    /// <summary>
    /// Returns every supported column once: saved order is preserved, unknown ids are dropped,
    /// and columns missing from an older saved configuration are appended in catalog order.
    /// </summary>
    public static List<DetailsColumnSettingDocument> Normalize(IEnumerable<DetailsColumnSettingDocument>? settings)
    {
        var normalized = new List<DetailsColumnSettingDocument>(All.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var setting in settings ?? [])
        {
            var definition = Find(setting.Id);
            if (definition is null || !seen.Add(definition.Id))
            {
                continue;
            }

            normalized.Add(new DetailsColumnSettingDocument
            {
                Id = definition.Id,
                IsVisible = definition.IsRequired || setting.IsVisible
            });
        }

        foreach (var definition in All)
        {
            if (!seen.Add(definition.Id))
            {
                continue;
            }

            normalized.Add(new DetailsColumnSettingDocument
            {
                Id = definition.Id,
                IsVisible = definition.IsRequired || definition.IsVisibleByDefault
            });
        }

        return normalized;
    }

    public static IReadOnlyList<BrowserDetailsColumn> CreateVisibleColumns(
        IReadOnlyList<DetailsColumnSettingDocument> settings)
    {
        var normalized = Normalize(settings);
        var columns = new List<BrowserDetailsColumn>();
        foreach (var setting in normalized)
        {
            if (!setting.IsVisible)
            {
                continue;
            }

            var definition = Find(setting.Id);
            if (definition is not null)
            {
                columns.Add(definition.CreateColumn());
            }
        }

        return columns;
    }
}
