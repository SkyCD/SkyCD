using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Documents;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public partial class MainWindowViewModel
{
    private IReadOnlyList<ToolbarItemDocument> toolbarEntries = ToolbarLayout.CreateDefault();

    /// <summary>The layout currently rendered in the toolbar, always normalized.</summary>
    public IReadOnlyList<ToolbarItemDocument> ToolbarEntries => toolbarEntries;

    /// <summary>Raised when the toolbar should be rebuilt, for example after a layout or language change.</summary>
    public event EventHandler? ToolbarItemsChanged;

    /// <summary>
    /// Raised when a toolbar toggle changes state, so the rendered buttons can update in place instead of
    /// rebuilding the whole toolbar.
    /// </summary>
    public event EventHandler? ToolbarStateChanged;

    private void RaiseToolbarStateChanged() => ToolbarStateChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Raised when the user confirmed a new layout; the view persists it.</summary>
    public event EventHandler? ToolbarLayoutChanged;

    /// <summary>Restores the saved layout without persisting it back, used when a window takes over the view model.</summary>
    public void LoadToolbarLayout(IEnumerable<ToolbarItemDocument>? layout)
    {
        toolbarEntries = ToolbarLayout.Normalize(layout, ToolbarCommandIds.All);
        ToolbarItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Applies a layout confirmed in the customization dialog.</summary>
    public void ApplyToolbarLayout(IEnumerable<ToolbarItemDocument> layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        toolbarEntries = ToolbarLayout.Normalize(layout, ToolbarCommandIds.All);
        ToolbarItemsChanged?.Invoke(this, EventArgs.Empty);
        ToolbarLayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The commands the toolbar can host, grouped for the customization dialog. Labels are resolved on each
    /// call so a language change is reflected; an unavailable command simply renders disabled. Every command
    /// carries an icon so a toolbar button never falls back to text only.
    /// </summary>
    public IReadOnlyList<ToolbarCommandDescriptor> ToolbarCommands()
    {
        var file = ToolbarCategory(UiKeys.FileMenu);
        var edit = ToolbarCategory(UiKeys.EditMenu);
        var view = ToolbarCategory(UiKeys.ViewMenu);
        var tools = ToolbarCategory(UiKeys.ToolsMenu);
        return
        [
            Descriptor(ToolbarCommandIds.NewCatalog, file, TextKeys.New, NewCatalogCommand, icon: ToolbarIconKeys.NewCatalog),
            Descriptor(ToolbarCommandIds.OpenCatalog, file, TextKeys.Open, OpenCatalogCommand, icon: ToolbarIconKeys.OpenCatalog),
            Descriptor(ToolbarCommandIds.SaveCatalog, file, TextKeys.Save, SaveCatalogCommand, icon: ToolbarIconKeys.SaveCatalog),
            Descriptor(ToolbarCommandIds.SaveCatalogAs, file, TextKeys.SaveAs, SaveCatalogAsCommand, icon: ToolbarIconKeys.SaveCatalogAs),
            Descriptor(ToolbarCommandIds.Undo, edit, TextKeys.Undo, UndoCommand, icon: ToolbarIconKeys.Undo),
            Descriptor(ToolbarCommandIds.Redo, edit, TextKeys.Redo, RedoCommand, icon: ToolbarIconKeys.Redo),
            Descriptor(ToolbarCommandIds.AddItem, edit, TextKeys.Add, AddItemCommand, icon: ToolbarIconKeys.AddItem),
            Descriptor(ToolbarCommandIds.DeleteItem, edit, TextKeys.Delete, DeleteItemCommand, icon: ToolbarIconKeys.DeleteItem),
            Descriptor(ToolbarCommandIds.Properties, edit, TextKeys.Properties, OpenPropertiesCommand, icon: ToolbarIconKeys.Properties),
            Descriptor(ToolbarCommandIds.ViewTiles, view, TextKeys.Tiles, SetViewModeCommand, "Tiles", ToolbarIconKeys.ViewTiles,
                isToggle: true, isChecked: IsTilesViewChecked),
            Descriptor(ToolbarCommandIds.ViewSmallIcons, view, TextKeys.SmallIcons2, SetViewModeCommand, "SmallIcons", ToolbarIconKeys.ViewSmallIcons,
                isToggle: true, isChecked: IsSmallIconsViewChecked),
            Descriptor(ToolbarCommandIds.ViewLargeIcons, view, TextKeys.LargeIconsMenu, SetViewModeCommand, "LargeIcons", ToolbarIconKeys.ViewLargeIcons,
                isToggle: true, isChecked: IsLargeIconsViewChecked),
            Descriptor(ToolbarCommandIds.ViewList, view, TextKeys.List, SetViewModeCommand, "List", ToolbarIconKeys.ViewList,
                isToggle: true, isChecked: IsListViewChecked),
            Descriptor(ToolbarCommandIds.ViewDetails, view, TextKeys.Details, SetViewModeCommand, "Details", ToolbarIconKeys.ViewDetails,
                isToggle: true, isChecked: IsDetailsViewChecked),
            Descriptor(ToolbarCommandIds.StatusBar, view, TextKeys.StatusBar, ToggleStatusBarCommand, icon: ToolbarIconKeys.StatusBar,
                isToggle: true, isChecked: IsStatusBarVisible),
            Descriptor(ToolbarCommandIds.PreviewPane, view, PreviewKeys.PreviewPane, TogglePreviewPaneCommand, icon: ToolbarIconKeys.PreviewPane,
                isToggle: true, isChecked: IsPreviewPaneVisible),
            Descriptor(ToolbarCommandIds.Refresh, tools, TextKeys.Refresh, RefreshCommand, icon: ToolbarIconKeys.Refresh),
            Descriptor(ToolbarCommandIds.Find, tools, TextKeys.Find, ToggleQuickFilterCommand, icon: ToolbarIconKeys.Find,
                isToggle: true, isChecked: IsQuickFilterOpen),
            Descriptor(ToolbarCommandIds.AdvancedFind, tools, TextKeys.AdvancedFind, AdvancedFindCatalogCommand, icon: ToolbarIconKeys.AdvancedFind),
            Descriptor(ToolbarCommandIds.Options, tools, UiKeys.Options, OpenOptionsCommand, icon: ToolbarIconKeys.Options),
            Descriptor(ToolbarCommandIds.Tags, tools, UiKeys.ManageTags, OpenTagsDialogCommand, icon: ToolbarIconKeys.Tags),
            Descriptor(ToolbarCommandIds.Collections, tools, UiKeys.ManageCollections, OpenCollectionsDialogCommand, icon: ToolbarIconKeys.Collections),
            Descriptor(ToolbarCommandIds.SaveSearchAsCollection, tools, TextKeys.SaveCurrentSearchAsCollection, SaveCurrentSearchAsCollectionCommand, icon: ToolbarIconKeys.SaveSearchAsCollection)
        ];
    }

    private static ToolbarCommandDescriptor Descriptor(
        string id,
        string category,
        string labelKey,
        System.Windows.Input.ICommand command,
        object? parameter = null,
        string? icon = null,
        bool isToggle = false,
        bool isChecked = false)
    {
        return new ToolbarCommandDescriptor
        {
            Id = id,
            Category = category,
            Label = LocalizationService.Current.Get(labelKey),
            IconPath = icon,
            Command = command,
            CommandParameter = parameter,
            IsToggle = isToggle,
            IsChecked = isChecked
        };
    }

    private static string ToolbarCategory(string menuKey) =>
        MnemonicText.Strip(LocalizationService.Current.Get(menuKey));
}
