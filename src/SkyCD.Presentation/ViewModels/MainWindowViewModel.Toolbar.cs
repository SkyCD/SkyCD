using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Documents;
using SkyCD.Plugin.Host.Catalog;
using SkyCD.SDK.Capabilities.Catalog;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public partial class MainWindowViewModel
{
    private IReadOnlyList<ToolbarItemDocument> toolbarEntries = ToolbarLayout.CreateDefault();

    public const string FileToolbarId = "file";
    private readonly Dictionary<string, List<ToolbarItemDocument>> pluginToolbarLayouts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AsyncRelayCommand> toolbarPluginCommands = [];
    private string? selectedToolbarId;

    public IReadOnlyList<ToolbarContribution> PluginToolbars =>
        (catalogFeatures?.GetToolbars() ?? [])
        .Where(toolbar => !string.IsNullOrWhiteSpace(toolbar.Id)
            && !toolbar.Id.Equals(FileToolbarId, StringComparison.OrdinalIgnoreCase))
        .ToArray();

    private ToolbarContribution? ActivePluginToolbar =>
        PluginToolbars.FirstOrDefault(toolbar => toolbar.Id.Equals(selectedToolbarId, StringComparison.OrdinalIgnoreCase));

    public string ActiveToolbarId => ActivePluginToolbar?.Id ?? FileToolbarId;

    public IReadOnlyList<ToolbarItemDocument> DefaultToolbarEntries =>
        ActivePluginToolbar is { } toolbar ? PluginDefaultLayout(toolbar) : ToolbarLayout.CreateDefault();

    public IReadOnlyList<ToolbarItemDocument> ToolbarEntries =>
        ActivePluginToolbar is { } toolbar
            ? ToolbarLayout.Normalize(pluginToolbarLayouts.GetValueOrDefault(toolbar.Id),
                toolbar.Buttons.Select(button => button.CommandId).ToArray(), PluginDefaultLayout(toolbar))
            : toolbarEntries;

    private static IReadOnlyList<ToolbarItemDocument> PluginDefaultLayout(ToolbarContribution toolbar) =>
        toolbar.Buttons.Select(button => ToolbarItemDocument.Command(button.CommandId)).ToArray();

    private IReadOnlyList<MainMenuItemViewModel> BuildToolbarMenuItems()
    {
        MainMenuItemViewModel Item(string id, string title) => new()
        {
            Header = title,
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = id == ActiveToolbarId,
            Command = new RelayCommand(() =>
            {
                selectedToolbarId = id;
                RefreshToolbarMenu();
                RaiseToolbarItemsChanged();
                ToolbarLayoutChanged?.Invoke(this, EventArgs.Empty);
            })
        };

        return
        [
            Item(FileToolbarId, ToolbarCategory(UiKeys.FileMenu)),
            .. PluginToolbars.Select(toolbar => Item(toolbar.Id, toolbar.Title))
        ];
    }

    private void RefreshToolbarMenu()
    {
        var menu = FlattenMenuItems(topMenuItems).FirstOrDefault(item => item.Key == "view_toolbars");
        if (menu is not null)
        {
            menu.Items = BuildToolbarMenuItems();
        }
    }

    private void RaiseToolbarItemsChanged()
    {
        toolbarPluginCommands.Clear();
        ToolbarItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshToolbarCommandState()
    {
        foreach (var command in toolbarPluginCommands)
        {
            command.NotifyCanExecuteChanged();
        }
    }

    public void SaveToolbarSettings(AppOptionsDocument options)
    {
        options.Toolbar = toolbarEntries.ToList();
        options.ActiveToolbarId = selectedToolbarId;
        options.PluginToolbarLayouts = pluginToolbarLayouts.Select(pair => new PluginToolbarLayoutDocument
        {
            ToolbarId = pair.Key,
            Items = pair.Value.ToList()
        }).ToList();
    }

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
    public void LoadToolbarLayout(IEnumerable<ToolbarItemDocument>? layout,
        string? toolbarId = null, IReadOnlyList<PluginToolbarLayoutDocument>? layouts = null)
    {
        toolbarEntries = ToolbarLayout.Normalize(layout, ToolbarCommandIds.All);
        selectedToolbarId = toolbarId;
        pluginToolbarLayouts.Clear();
        if (layouts is not null)
        {
            foreach (var saved in layouts)
            {
                pluginToolbarLayouts[saved.ToolbarId] = saved.Items.ToList();
            }
        }

        RefreshToolbarMenu();
        RaiseToolbarItemsChanged();
    }

    public void SetStoredToolbarLayout(AppOptionsDocument options, IEnumerable<ToolbarItemDocument> layout)
    {
        if (ActivePluginToolbar is { } toolbar)
        {
            options.PluginToolbarLayouts.RemoveAll(saved =>
                saved.ToolbarId.Equals(toolbar.Id, StringComparison.OrdinalIgnoreCase));
            options.PluginToolbarLayouts.Add(new PluginToolbarLayoutDocument
            {
                ToolbarId = toolbar.Id,
                Items = ToolbarLayout.Normalize(layout,
                    toolbar.Buttons.Select(button => button.CommandId).ToArray(), PluginDefaultLayout(toolbar)).ToList()
            });
        }
        else
        {
            options.Toolbar = ToolbarLayout.Normalize(layout, ToolbarCommandIds.All).ToList();
        }
    }

    /// <summary>Applies a layout confirmed in the customization dialog.</summary>
    public void ApplyToolbarLayout(IEnumerable<ToolbarItemDocument> layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (ActivePluginToolbar is { } toolbar)
        {
            pluginToolbarLayouts[toolbar.Id] = ToolbarLayout.Normalize(layout,
                toolbar.Buttons.Select(button => button.CommandId).ToArray(), PluginDefaultLayout(toolbar)).ToList();
        }
        else
        {
            toolbarEntries = ToolbarLayout.Normalize(layout, ToolbarCommandIds.All);
        }

        RaiseToolbarItemsChanged();
        ToolbarLayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The commands the toolbar can host, grouped for the customization dialog. Labels are resolved on each
    /// call so a language change is reflected; an unavailable command simply renders disabled. Every command
    /// carries an icon so a toolbar button never falls back to text only.
    /// </summary>
    public IReadOnlyList<ToolbarCommandDescriptor> ToolbarCommands(bool trackSelection = false) =>
        ActivePluginToolbar is { } toolbar ? BuildPluginToolbarCommands(toolbar, trackSelection) : FileToolbarCommands();

    private IReadOnlyList<ToolbarCommandDescriptor> BuildPluginToolbarCommands(ToolbarContribution toolbar, bool trackSelection)
    {
        var commands = catalogFeatures!.GetEntryCommands().ToDictionary(command => command.CommandId, StringComparer.OrdinalIgnoreCase);
        return toolbar.Buttons.DistinctBy(button => button.CommandId, StringComparer.OrdinalIgnoreCase)
            .Where(button => commands.ContainsKey(button.CommandId))
            .Select(button =>
            {
                var contribution = commands[button.CommandId];
                var command = new AsyncRelayCommand(() => ExecuteFeatureEntryCommandAsync(contribution.CommandId),
                    () => CanExecuteFeatureCommand(contribution));
                if (trackSelection)
                {
                    toolbarPluginCommands.Add(command);
                }
                return new ToolbarCommandDescriptor
                {
                    Id = contribution.CommandId,
                    Category = toolbar.Title,
                    Label = string.IsNullOrWhiteSpace(button.Label) ? contribution.Title : button.Label,
                    IconPath = button.IconKey,
                    Command = command
                };
            }).ToArray();
    }

    private IReadOnlyList<ToolbarCommandDescriptor> FileToolbarCommands()
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
