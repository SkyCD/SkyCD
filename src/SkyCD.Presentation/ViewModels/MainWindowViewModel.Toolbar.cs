using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
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

    /// <summary>Opens Options on the Toolbar category instead of a standalone dialog.</summary>
    [RelayCommand]
    private void CustomizeToolbar()
    {
        ShowOptions(LocalizationService.Current.Get(UiKeys.Toolbar));
    }

    /// <summary>
    /// The commands the toolbar can host, grouped for the customization dialog. Labels are resolved on each
    /// call so a language change is reflected; an unavailable command simply renders disabled.
    /// </summary>
    public IReadOnlyList<ToolbarCommandDescriptor> ToolbarCommands()
    {
        var file = ToolbarCategory(UiKeys.FileMenu);
        var edit = ToolbarCategory(UiKeys.EditMenu);
        var view = ToolbarCategory(UiKeys.ViewMenu);
        var tools = ToolbarCategory(UiKeys.ToolsMenu);
        return
        [
            Descriptor(ToolbarCommandIds.NewCatalog, file, TextKeys.New, NewCatalogCommand, icon: "/Assets/new.png"),
            Descriptor(ToolbarCommandIds.OpenCatalog, file, TextKeys.Open, OpenCatalogCommand, icon: "/Assets/open.png"),
            Descriptor(ToolbarCommandIds.SaveCatalog, file, TextKeys.Save, SaveCatalogCommand, icon: "/Assets/save.png"),
            Descriptor(ToolbarCommandIds.SaveCatalogAs, file, TextKeys.SaveAs, SaveCatalogAsCommand),
            Descriptor(ToolbarCommandIds.Undo, edit, TextKeys.Undo, UndoCommand),
            Descriptor(ToolbarCommandIds.Redo, edit, TextKeys.Redo, RedoCommand),
            Descriptor(ToolbarCommandIds.AddItem, edit, TextKeys.Add, AddItemCommand),
            Descriptor(ToolbarCommandIds.DeleteItem, edit, TextKeys.Delete, DeleteItemCommand),
            Descriptor(ToolbarCommandIds.Properties, edit, TextKeys.Properties, OpenPropertiesCommand),
            Descriptor(ToolbarCommandIds.ViewTiles, view, TextKeys.Tiles, SetViewModeCommand, "Tiles"),
            Descriptor(ToolbarCommandIds.ViewSmallIcons, view, TextKeys.SmallIcons2, SetViewModeCommand, "SmallIcons"),
            Descriptor(ToolbarCommandIds.ViewLargeIcons, view, TextKeys.LargeIconsMenu, SetViewModeCommand, "LargeIcons"),
            Descriptor(ToolbarCommandIds.ViewList, view, TextKeys.List, SetViewModeCommand, "List"),
            Descriptor(ToolbarCommandIds.ViewDetails, view, TextKeys.Details, SetViewModeCommand, "Details"),
            Descriptor(ToolbarCommandIds.StatusBar, view, TextKeys.StatusBar, ToggleStatusBarCommand),
            Descriptor(ToolbarCommandIds.PreviewPane, view, PreviewKeys.PreviewPane, TogglePreviewPaneCommand),
            Descriptor(ToolbarCommandIds.Refresh, tools, TextKeys.Refresh, RefreshCommand),
            Descriptor(ToolbarCommandIds.Find, tools, TextKeys.Find, FindCatalogCommand),
            Descriptor(ToolbarCommandIds.AdvancedFind, tools, TextKeys.AdvancedFind, AdvancedFindCatalogCommand),
            Descriptor(ToolbarCommandIds.Options, tools, UiKeys.Options, OpenOptionsCommand),
            Descriptor(ToolbarCommandIds.Tags, tools, UiKeys.ManageTags, OpenTagsDialogCommand),
            Descriptor(ToolbarCommandIds.Collections, tools, UiKeys.ManageCollections, OpenCollectionsDialogCommand),
            Descriptor(ToolbarCommandIds.Contacts, tools, UiKeys.ManageContacts, OpenContactsDialogCommand),
            Descriptor(ToolbarCommandIds.Locations, tools, UiKeys.ManageLocations, OpenLocationsDialogCommand),
            Descriptor(ToolbarCommandIds.AiDescriptions, tools, TextKeys.GenerateAiDescriptions, GenerateAiDescriptionsCommand)
        ];
    }

    private static ToolbarCommandDescriptor Descriptor(
        string id,
        string category,
        string labelKey,
        System.Windows.Input.ICommand command,
        object? parameter = null,
        string? icon = null)
    {
        return new ToolbarCommandDescriptor
        {
            Id = id,
            Category = category,
            Label = LocalizationService.Current.Get(labelKey),
            IconPath = icon,
            Command = command,
            CommandParameter = parameter
        };
    }

    private static string ToolbarCategory(string menuKey) =>
        LocalizationService.Current.Get(menuKey).Replace("_", string.Empty).Replace("&", string.Empty);
}
