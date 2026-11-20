using System;
using System.Collections.Generic;
using SkyCD.Documents;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// Stable identifiers of the commands the toolbar can host. A persisted layout refers to these ids, so
/// renaming a command method or moving it between modules never breaks a saved toolbar.
/// </summary>
public static class ToolbarCommandIds
{
    public const string NewCatalog = "new-catalog";
    public const string OpenCatalog = "open-catalog";
    public const string SaveCatalog = "save-catalog";
    public const string SaveCatalogAs = "save-catalog-as";
    public const string Undo = "undo";
    public const string Redo = "redo";
    public const string AddItem = "add-item";
    public const string DeleteItem = "delete-item";
    public const string Properties = "properties";
    public const string ViewTiles = "view-tiles";
    public const string ViewSmallIcons = "view-small-icons";
    public const string ViewLargeIcons = "view-large-icons";
    public const string ViewList = "view-list";
    public const string ViewDetails = "view-details";
    public const string StatusBar = "status-bar";
    public const string PreviewPane = "preview-pane";
    public const string Refresh = "refresh";
    public const string Find = "find";
    public const string AdvancedFind = "advanced-find";
    public const string Options = "options";
    public const string Tags = "tags";
    public const string Collections = "collections";
    public const string Contacts = "contacts";
    public const string Locations = "locations";
    public const string AiDescriptions = "ai-descriptions";

    /// <summary>Every id a persisted layout may reference.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        NewCatalog,
        OpenCatalog,
        SaveCatalog,
        SaveCatalogAs,
        Undo,
        Redo,
        AddItem,
        DeleteItem,
        Properties,
        ViewTiles,
        ViewSmallIcons,
        ViewLargeIcons,
        ViewList,
        ViewDetails,
        StatusBar,
        PreviewPane,
        Refresh,
        Find,
        AdvancedFind,
        Options,
        Tags,
        Collections,
        Contacts,
        Locations,
        AiDescriptions
    ];
}

/// <summary>
/// The toolbar layout contract: which entries are valid, what a fresh install shows and how a saved or
/// hand-edited layout is repaired before it reaches the toolbar.
/// </summary>
public static class ToolbarLayout
{
    public const int MaxItems = 64;

    /// <summary>New, Open, Save, separator, Find — the toolbar the application shipped with.</summary>
    public static IReadOnlyList<ToolbarItemDocument> CreateDefault() =>
    [
        ToolbarItemDocument.Command(ToolbarCommandIds.NewCatalog),
        ToolbarItemDocument.Command(ToolbarCommandIds.OpenCatalog),
        ToolbarItemDocument.Command(ToolbarCommandIds.SaveCatalog),
        ToolbarItemDocument.Separator(),
        ToolbarItemDocument.Command(ToolbarCommandIds.Find)
    ];

    /// <summary>
    /// Repairs a stored layout: unknown command ids — a command that no longer exists — are dropped, a command
    /// appears at most once, separators never lead, end or repeat, and the entry count is capped. A layout that
    /// contained entries but none survive falls back to the default, so a stale configuration cannot leave the
    /// user without a toolbar; a deliberately emptied layout is kept empty.
    /// </summary>
    public static IReadOnlyList<ToolbarItemDocument> Normalize(
        IEnumerable<ToolbarItemDocument>? layout,
        IReadOnlyCollection<string> knownCommandIds)
    {
        ArgumentNullException.ThrowIfNull(knownCommandIds);
        if (layout is null)
        {
            return Normalize(CreateDefault(), knownCommandIds);
        }

        var known = new HashSet<string>(knownCommandIds, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<ToolbarItemDocument>();
        var hadEntries = false;
        foreach (var entry in layout)
        {
            if (entry is null)
            {
                continue;
            }

            hadEntries = true;
            if (normalized.Count == MaxItems)
            {
                break;
            }

            if (IsSeparator(entry))
            {
                if (normalized.Count > 0 && !IsSeparator(normalized[^1]))
                {
                    normalized.Add(ToolbarItemDocument.Separator());
                }

                continue;
            }

            var commandId = entry.CommandId?.Trim();
            if (string.IsNullOrEmpty(commandId) || !known.Contains(commandId) || !seen.Add(commandId))
            {
                continue;
            }

            normalized.Add(ToolbarItemDocument.Command(commandId));
        }

        if (normalized.Count > 0 && IsSeparator(normalized[^1]))
        {
            normalized.RemoveAt(normalized.Count - 1);
        }

        return normalized.Count == 0 && hadEntries
            ? Normalize(CreateDefault(), knownCommandIds)
            : normalized;
    }

    private static bool IsSeparator(ToolbarItemDocument entry) =>
        string.Equals(entry.Kind, ToolbarItemDocument.SeparatorKind, StringComparison.OrdinalIgnoreCase);
}
