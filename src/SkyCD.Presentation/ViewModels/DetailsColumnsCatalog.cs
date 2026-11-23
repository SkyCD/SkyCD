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
    public const string LocationId = "location";

    public const double MinSavedColumnWidth = 30;

    public const double MaxSavedColumnWidth = 4000;

    private static readonly DetailsColumnDefinition[] CoreColumns =
    [
        new()
        {
            Id = NameId,
            HeaderKey = TextKeys.Name2,
            ValuePath = nameof(CatalogDocument.DisplayName),
            Width = new GridLength(500, GridUnitType.Pixel),
            IsRequired = true,
            ShowsItemIcon = true
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
            Id = LocationId,
            HeaderKey = UiKeys.Location,
            ValuePath = nameof(CatalogDocument.Location),
            Width = new GridLength(140, GridUnitType.Pixel)
        }
    ];

    private static DetailsColumnDefinition[] contributedColumns = [];

    /// <summary>
    /// Every supported column: the built-in catalog followed by columns contributed by enabled feature
    /// plugins. Contributed columns take part in saved visibility, width and ordering like built-in ones.
    /// </summary>
    public static IReadOnlyList<DetailsColumnDefinition> All =>
        contributedColumns.Length == 0 ? CoreColumns : [.. CoreColumns, .. contributedColumns];

    /// <summary>Installs the columns contributed by enabled plugins; an empty list removes them.</summary>
    public static void SetContributedColumns(IEnumerable<DetailsColumnDefinition>? columns)
    {
        contributedColumns = columns?.ToArray() ?? [];
    }

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
                IsVisible = definition.IsRequired || setting.IsVisible,
                Width = ToSavedWidth(setting.Width)
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
                columns.Add(definition.CreateColumn(setting.Width));
            }
        }

        return columns;
    }

    /// <summary>
    /// Moves a visible column to <paramref name="targetVisibleIndex"/> inside the visible order. Hidden
    /// columns keep their slots in the saved configuration (and their saved widths). Required columns
    /// remain visible but, like every other column, can be moved to any position.
    /// </summary>
    public static List<DetailsColumnSettingDocument> MoveVisibleColumn(
        IEnumerable<DetailsColumnSettingDocument>? settings,
        string columnId,
        int targetVisibleIndex)
    {
        var normalized = Normalize(settings);
        var definition = Find(columnId);
        if (definition is null)
        {
            return normalized;
        }

        var visibleSlots = normalized
            .Select((setting, index) => (setting, index))
            .Where(static pair => pair.setting.IsVisible)
            .ToList();
        var sourceVisibleIndex = visibleSlots.FindIndex(pair =>
            string.Equals(pair.setting.Id, definition.Id, StringComparison.OrdinalIgnoreCase));
        if (sourceVisibleIndex < 0)
        {
            return normalized;
        }

        var clampedTarget = Math.Clamp(targetVisibleIndex, 0, visibleSlots.Count - 1);
        if (clampedTarget == sourceVisibleIndex)
        {
            return normalized;
        }

        var visibleSettings = visibleSlots.Select(static pair => pair.setting).ToList();
        var moved = visibleSettings[sourceVisibleIndex];
        visibleSettings.RemoveAt(sourceVisibleIndex);
        visibleSettings.Insert(clampedTarget, moved);

        for (var i = 0; i < visibleSlots.Count; i++)
        {
            normalized[visibleSlots[i].index] = visibleSettings[i];
        }

        return normalized;
    }

    /// <summary>
    /// Stores a user-chosen pixel width for the column, clamped to a sane range. The normalized list is
    /// returned unchanged when the id is unknown.
    /// </summary>
    public static List<DetailsColumnSettingDocument> SetColumnWidth(
        IEnumerable<DetailsColumnSettingDocument>? settings,
        string columnId,
        double width)
    {
        var normalized = Normalize(settings);
        var definition = Find(columnId);
        if (definition is null || double.IsNaN(width))
        {
            return normalized;
        }

        var setting = normalized.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, definition.Id, StringComparison.OrdinalIgnoreCase));
        if (setting is null)
        {
            return normalized;
        }

        setting.Width = Math.Clamp(width, MinSavedColumnWidth, MaxSavedColumnWidth);
        return normalized;
    }

    private static double? ToSavedWidth(double? width)
    {
        return width is > 0 && double.IsFinite(width.Value) ? width : null;
    }
}
