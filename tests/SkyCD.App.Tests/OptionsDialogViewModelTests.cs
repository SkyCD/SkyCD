using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Documents;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

public class OptionsDialogViewModelTests
{
    [Fact]
    public void Constructor_InitializesLanguageSelection()
    {
        var vm = new OptionsDialogViewModel(["English", "Lithuanian"]);

        Assert.Equal(2, vm.Languages.Count);
        Assert.Equal("English", vm.SelectedLanguage?.Name);
    }

    [Fact]
    public void RefreshPluginsCommand_RaisesRefreshRequest()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        var raised = false;
        vm.RefreshPluginsRequested += (_, _) => raised = true;

        vm.RefreshPluginsCommand.Execute(null);

        Assert.True(raised);
    }

    [Fact]
    public void SetPlugins_SelectsFirstPlugin()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        var plugins = new[]
        {
            new OptionsPluginItem("JSON", "IFileFormatPluginCapability", "skycd.plugin.json v2.0.0"),
            new OptionsPluginItem("XML", "IFileFormatPluginCapability", "skycd.plugin.xml v2.0.0")
        };

        vm.SetPlugins(plugins);

        Assert.Equal(2, vm.Plugins.Count);
        Assert.Equal("JSON", vm.SelectedPlugin?.Name);
    }

    [Fact]
    public void OptionsPluginItem_BuildsIconKeyFromPrimaryExtensionForFileFormats()
    {
        var plugin = new OptionsPluginItem("JSON", "SkyCD", "json v2.0.0", primaryExtension: "json",
            iconKind: PluginIconKind.FileFormat);

        Assert.Equal("json", plugin.PrimaryExtension);
        Assert.Equal("ext:json", plugin.IconKey);
        Assert.True(plugin.HasBitmapIcon);
        Assert.False(plugin.HasGlyphIcon);
    }

    [Fact]
    public void OptionsPluginItem_PrefersDeclaredIconOverTheExtension()
    {
        var plugin = new OptionsPluginItem("JSON", "SkyCD", "json v2.0.0", primaryExtension: "json",
            iconKind: PluginIconKind.FileFormat, icon: "Lucide:FileText");

        Assert.Equal("Lucide:FileText", plugin.IconKey);
        Assert.True(plugin.HasGlyphIcon);
        Assert.False(plugin.HasBitmapIcon);
    }

    [Theory]
    [InlineData(PluginIconKind.Metadata, "lucide:Tag")]
    [InlineData(PluginIconKind.Preview, "lucide:Eye")]
    [InlineData(PluginIconKind.Accounts, "lucide:User")]
    [InlineData(PluginIconKind.Ai, "lucide:Sparkles")]
    [InlineData(PluginIconKind.Feature, "lucide:Puzzle")]
    public void OptionsPluginItem_UsesTheCapabilityKindFallback(
        PluginIconKind kind, string expected)
    {
        var plugin = new OptionsPluginItem("Plugin", "SkyCD", "plugin v2.0.0", iconKind: kind);

        Assert.Equal(expected, plugin.IconKey);
        Assert.True(plugin.HasGlyphIcon);
        Assert.False(plugin.HasBitmapIcon);
    }

    [Fact]
    public void OptionsPluginItem_UsesADocumentIconForFileFormatsWithoutAnExtension()
    {
        var plugin = new OptionsPluginItem("Catalog", "SkyCD", "catalog v2.0.0",
            iconKind: PluginIconKind.FileFormat);

        Assert.Equal("file", plugin.IconKey);
        Assert.True(plugin.HasBitmapIcon);
        Assert.False(plugin.HasGlyphIcon);
    }

    [Fact]
    public void SetPlugins_RespectsPreviouslyDisabledPluginIds()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetDisabledPluginIds(["plugin.xml"]);

        vm.SetPlugins(
        [
            new OptionsPluginItem("JSON", "IFileFormatPluginCapability", "json v2.0.0", id: "plugin.json"),
            new OptionsPluginItem("XML", "IFileFormatPluginCapability", "xml v2.0.0", id: "plugin.xml")
        ]);

        Assert.True(vm.Plugins.Single(plugin => plugin.Id == "plugin.json").IsEnabled);
        Assert.False(vm.Plugins.Single(plugin => plugin.Id == "plugin.xml").IsEnabled);
    }

    [Fact]
    public void GetDisabledPluginIds_ReturnsUncheckedPluginIds()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetPlugins(
        [
            new OptionsPluginItem("JSON", "IFileFormatPluginCapability", "json v2.0.0", id: "plugin.json"),
            new OptionsPluginItem("XML", "IFileFormatPluginCapability", "xml v2.0.0", id: "plugin.xml")
        ]);

        vm.Plugins.Single(plugin => plugin.Id == "plugin.xml").IsEnabled = false;

        var disabled = vm.GetDisabledPluginIds();

        Assert.Equal(["plugin.xml"], disabled);
    }

    [Fact]
    public void SelectedTabIndex_CanBeUpdated()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        vm.SelectedTabIndex = 2;

        Assert.Equal(2, vm.SelectedTabIndex);
    }

    [Fact]
    public void SelectedTabIndex_ClampsOutOfRangeValues()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        vm.SelectedTabIndex = -5;
        Assert.Equal(0, vm.SelectedTabIndex);

        vm.SelectedTabIndex = 99;
        Assert.Equal(vm.SettingCategories.Count - 1, vm.SelectedTabIndex);
    }

    [Fact]
    public void SearchText_FiltersPluginSections()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SelectedTabIndex = 0;

        vm.SettingsSearchText = "plug";

        Assert.Equal(["Plugins"], vm.FilteredSettingCategories.Select(category => category.Title));
        Assert.True(vm.ShowPluginPathSection);
        Assert.True(vm.ShowPluginListSection);
        Assert.True(vm.ShowPluginActionsSection);
        Assert.True(vm.ShowPluginInfoSection);
        Assert.True(vm.HasVisibleCategoryContent);
        Assert.False(vm.ShowNoSearchResults);
    }

    [Fact]
    public void SearchText_ShowsNoResultsWhenCategoryHasNoMatch()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SelectedTabIndex = 0;

        vm.SettingsSearchText = "terminal";

        Assert.Empty(vm.FilteredSettingCategories);
        Assert.False(vm.ShowPluginPathSection);
        Assert.False(vm.HasVisibleCategoryContent);
        Assert.True(vm.ShowNoSearchResults);
    }

    [Fact]
    public void SearchText_FiltersLeftCategoryList()
    {
        var vm = new OptionsDialogViewModel(["English", "Lithuanian"]);

        vm.SettingsSearchText = "lang";

        Assert.Equal(["Language"], vm.FilteredSettingCategories.Select(category => category.Title));
        Assert.Equal("Language", vm.SelectedSettingCategory?.Title);
        Assert.Equal(1, vm.SelectedTabIndex);
        Assert.True(vm.ShowLanguageSection);
    }

    [Fact]
    public void SearchText_FiltersMcpCategory()
    {
        var vm = new OptionsDialogViewModel(["English", "Lithuanian"]);

        vm.SettingsSearchText = "mcp";

        // The MCP tool exposure page shares the server keywords, so both MCP pages match.
        Assert.Equal(["MCP", "MCP Tools"], vm.FilteredSettingCategories.Select(category => category.Title));
        Assert.Equal("MCP", vm.SelectedSettingCategory?.Title);
        Assert.Equal(7, vm.SelectedTabIndex);
        Assert.True(vm.ShowMcpSection);
    }

    [Fact]
    public void SearchText_FiltersStatusesCategory()
    {
        var vm = new OptionsDialogViewModel(["English", "Lithuanian"]);

        vm.SettingsSearchText = "status";

        Assert.Equal(["Statuses"], vm.FilteredSettingCategories.Select(category => category.Title));
        Assert.Equal("Statuses", vm.SelectedSettingCategory?.Title);
        Assert.Equal(2, vm.SelectedTabIndex);
        Assert.True(vm.ShowStatusesSection);
    }

    [Fact]
    public void SearchText_DoesNotFilterRightPanelItemCollections()
    {
        var vm = new OptionsDialogViewModel(["English", "Lithuanian"]);
        vm.SetPlugins(
        [
            new OptionsPluginItem("JSON", "IFileFormatPluginCapability", "json v2.0.0", id: "plugin.json"),
            new OptionsPluginItem("XML", "IFileFormatPluginCapability", "xml v2.0.0", id: "plugin.xml")
        ]);
        vm.SelectedTabIndex = 0;

        vm.SettingsSearchText = "plug";

        Assert.Equal(2, vm.Plugins.Count);
        Assert.Equal(2, vm.Languages.Count);
    }

    [Fact]
    public void McpPort_IsClampedAndBuildsUrl()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        vm.McpPort = 0;
        Assert.Equal(1, vm.McpPort);

        vm.McpPort = 70000;
        Assert.Equal(65535, vm.McpPort);

        vm.McpPort = 8787;
        Assert.Equal("http://127.0.0.1:8787/mcp", vm.McpBaseUrl);
    }

    [Fact]
    public void McpServer_CanBeDisabled()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        vm.IsMcpServerEnabled = false;

        Assert.False(vm.IsMcpServerEnabled);
    }

    [Fact]
    public void McpStatusIconVisibility_DefaultsToTrueAndCanBeDisabled()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        Assert.True(vm.IsMcpStatusIconVisible);
        Assert.Equal("Copy URL", vm.McpCopyTooltip);
        Assert.False(vm.ShowMcpAlert);
        vm.IsMcpStatusIconVisible = false;
        Assert.False(vm.IsMcpStatusIconVisible);
    }

    [Fact]
    public void StatusVariants_CanBeAddedRemovedAndExported()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetStatusVariants(
        [
            new StatusVariantDocument
            {
                Name = "Watched",
                IconGlyph = "check",
                ItemTypes = [SkyCD.Documents.Enum.CatalogDocumentType.Media]
            }
        ]);

        Assert.Single(vm.StatusVariants);
        Assert.True(vm.RemoveStatusVariantCommand.CanExecute(null));

        vm.AddStatusVariantCommand.Execute(null);
        Assert.Equal(2, vm.StatusVariants.Count);

        vm.SelectedStatusVariant = vm.StatusVariants[0];
        vm.RemoveStatusVariantCommand.Execute(null);
        Assert.Single(vm.StatusVariants);

        var exported = vm.GetStatusVariants();
        Assert.Single(exported);
        Assert.False(string.IsNullOrWhiteSpace(exported[0].Name));
        Assert.Contains(SkyCD.Documents.Enum.CatalogDocumentType.Media, exported[0].ItemTypes!);
    }

    [Fact]
    public void ConfirmCommand_ShowsStatusAlert_WhenAnyStatusIconIsMissing()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.AddStatusVariantCommand.Execute(null);

        vm.ConfirmCommand.Execute(null);

        Assert.False(vm.DialogAccepted);
        Assert.True(vm.ShowStatusAlert);
        Assert.Equal("All status items must have an icon selected.", vm.StatusAlertMessage);
    }

    [Fact]
    public void ConfirmCommand_Accepts_WhenAllStatusIconsAreSet()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetStatusVariants(
        [
            new StatusVariantDocument { Name = "Watched", IconGlyph = "check" }
        ]);

        vm.ConfirmCommand.Execute(null);

        Assert.True(vm.DialogAccepted);
        Assert.False(vm.ShowStatusAlert);
    }

    [Fact]
    public void CustomProperties_CanBeAddedRemovedAndExported()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetCustomProperties(
        [
            new CustomPropertyDocument { Name = "Genre", DefaultValue = "Rock" }
        ]);

        Assert.Single(vm.CustomProperties);
        Assert.Equal("Genre", vm.SelectedCustomProperty?.Name);
        Assert.False(vm.ShowCustomPropertyAlert);

        vm.AddCustomPropertyCommand.Execute(null);
        Assert.Equal(2, vm.CustomProperties.Count);

        vm.RemoveCustomPropertyCommand.Execute(null);
        Assert.Single(vm.CustomProperties);

        var exported = vm.GetCustomProperties();
        Assert.Single(exported);
        Assert.False(string.IsNullOrWhiteSpace(exported[0].Name));
    }

    [Fact]
    public void Confirm_ShowsCustomPropertyAlert_WhenNameIsBlank()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.AddCustomPropertyCommand.Execute(null);

        vm.ConfirmCommand.Execute(null);

        Assert.False(vm.DialogAccepted);
        Assert.True(vm.ShowCustomPropertyAlert);
        Assert.False(string.IsNullOrWhiteSpace(vm.CustomPropertyAlertMessage));
    }

    [Fact]
    public void Confirm_ShowsCustomPropertyAlert_WhenNamesAreDuplicated()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetCustomProperties(
        [
            new CustomPropertyDocument { Name = "Genre" },
            new CustomPropertyDocument { Name = "genre" }
        ]);

        vm.ConfirmCommand.Execute(null);

        Assert.False(vm.DialogAccepted);
        Assert.True(vm.ShowCustomPropertyAlert);
    }

    [Fact]
    public void Confirm_ClearsCustomPropertyAlert_WhenNamesBecomeValid()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.AddCustomPropertyCommand.Execute(null);
        vm.ConfirmCommand.Execute(null);
        Assert.True(vm.ShowCustomPropertyAlert);

        vm.SelectedCustomProperty!.Name = "Genre";
        vm.ConfirmCommand.Execute(null);

        Assert.True(vm.DialogAccepted);
        Assert.False(vm.ShowCustomPropertyAlert);
        Assert.Equal(string.Empty, vm.CustomPropertyAlertMessage);
    }

    [Fact]
    public void ResetStatusVariantsCommand_RaisesRequestedEvent()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        var raised = false;
        vm.ResetStatusVariantsRequested += (_, _) => raised = true;

        vm.ResetStatusVariantsCommand.Execute(null);

        Assert.True(raised);
    }

    [Fact]
    public void StatusVariants_ShareIconCatalogAndPreserveUnknownGlyphs()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetStatusVariants(
        [
            new StatusVariantDocument { Name = "Known", IconGlyph = "Lucide:Star", IconColor = "#FF0000" },
            new StatusVariantDocument { Name = "Unknown", IconGlyph = "missing:icon", IconColor = "#00FF00" }
        ]);

        var first = vm.StatusVariants[0];
        var second = vm.StatusVariants[1];

        Assert.Same(first.IconOptions, second.IconOptions);
        Assert.Contains("Lucide:Star", first.IconOptions);
        Assert.Equal("missing:icon", second.IconGlyph);
        Assert.Equal("#00FF00", second.IconColor);

        first.IconGlyph = "Lucide:Check";

        var exported = vm.GetStatusVariants();
        Assert.Equal("Lucide:Check", exported[0].IconGlyph);
        Assert.Equal("#FF0000", exported[0].IconColor);
        Assert.Equal("missing:icon", exported[1].IconGlyph);
        Assert.Equal("#00FF00", exported[1].IconColor);
    }

    [Fact]
    public void StatusVariants_KeepSelectedIconWhenSavedAndReopened()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetStatusVariants(
        [
            new StatusVariantDocument { Name = "Watched", IconGlyph = "Lucide:Star" }
        ]);
        vm.StatusVariants[0].IconGlyph = "Lucide:Check";

        var reopened = new OptionsDialogViewModel(["English"]);
        reopened.SetStatusVariants(vm.GetStatusVariants());

        var restored = Assert.Single(reopened.StatusVariants);
        Assert.Equal("Lucide:Check", restored.IconGlyph);
        Assert.Contains("Lucide:Check", restored.IconOptions);
    }

    [Fact]
    public void StatusVariantItemType_AllowsMultipleSelections()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.AddStatusVariantCommand.Execute(null);

        var status = vm.StatusVariants.Single();
        status.SetTypeSelected(SkyCD.Documents.Enum.CatalogDocumentType.Media, true);
        status.SetTypeSelected(SkyCD.Documents.Enum.CatalogDocumentType.File, true);

        var exported = vm.GetStatusVariants().Single();
        Assert.Contains(SkyCD.Documents.Enum.CatalogDocumentType.Media, exported.ItemTypes!);
        Assert.Contains(SkyCD.Documents.Enum.CatalogDocumentType.File, exported.ItemTypes!);
    }

    [Fact]
    public void PluginFilterText_FiltersPluginsByName()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetPlugins(
        [
            new OptionsPluginItem("JSON", "IFileFormatPluginCapability", "json v2.0.0", id: "plugin.json"),
            new OptionsPluginItem("XML", "IFileFormatPluginCapability", "xml v2.0.0", id: "plugin.xml")
        ]);

        vm.PluginFilterText = "xm";

        Assert.Equal(["XML"], vm.FilteredPlugins.Select(plugin => plugin.Name).ToArray());
        Assert.Equal("XML", vm.SelectedPlugin?.Name);
        Assert.Equal(2, vm.Plugins.Count);
    }

    [Fact]
    public void PluginFilterText_IgnoresCase()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetPlugins(
        [
            new OptionsPluginItem("JSON", "IFileFormatPluginCapability", "json v2.0.0", id: "plugin.json"),
            new OptionsPluginItem("XML", "IFileFormatPluginCapability", "xml v2.0.0", id: "plugin.xml")
        ]);

        vm.PluginFilterText = "JsOn";

        Assert.Equal(["JSON"], vm.FilteredPlugins.Select(plugin => plugin.Name).ToArray());
        Assert.Equal("JSON", vm.SelectedPlugin?.Name);
    }

    [Fact]
    public void PluginFilterText_ClearingRestoresAllPlugins()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetPlugins(
        [
            new OptionsPluginItem("JSON", "IFileFormatPluginCapability", "json v2.0.0", id: "plugin.json"),
            new OptionsPluginItem("XML", "IFileFormatPluginCapability", "xml v2.0.0", id: "plugin.xml")
        ]);
        vm.PluginFilterText = "xml";

        vm.PluginFilterText = string.Empty;

        Assert.Equal(2, vm.FilteredPlugins.Count);
        Assert.Equal("JSON", vm.SelectedPlugin?.Name);
    }

    [Fact]
    public void PluginFilterText_WithoutMatchClearsSelection()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetPlugins(
        [
            new OptionsPluginItem("JSON", "IFileFormatPluginCapability", "json v2.0.0",
                id: "plugin.json")
        ]);

        vm.PluginFilterText = "terminal";

        Assert.Empty(vm.FilteredPlugins);
        Assert.Null(vm.SelectedPlugin);
    }

    [Fact]
    public void SetPlugins_AppliesActivePluginFilter()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.PluginFilterText = "xml";

        vm.SetPlugins(
        [
            new OptionsPluginItem("JSON", "IFileFormatPluginCapability", "json v2.0.0", id: "plugin.json"),
            new OptionsPluginItem("XML", "IFileFormatPluginCapability", "xml v2.0.0", id: "plugin.xml")
        ]);

        Assert.Equal(["XML"], vm.FilteredPlugins.Select(plugin => plugin.Name).ToArray());
        Assert.Equal("XML", vm.SelectedPlugin?.Name);
    }

    [Fact]
    public void SelectedPluginDescription_ShowsSelectedPluginDescription()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetPlugins(
        [
            new OptionsPluginItem("JSON", "IFileFormatPluginCapability", "json v2.0.0", id: "plugin.json", description: "Reads JSON catalogs"),
            new OptionsPluginItem("XML", "IFileFormatPluginCapability", "xml v2.0.0", id: "plugin.xml", description: "Reads XML catalogs")
        ]);

        Assert.Equal("Reads JSON catalogs", vm.SelectedPluginDescription);
    }

    [Fact]
    public void SelectedPluginDescription_UpdatesWhenSelectionChanges()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetPlugins(
        [
            new OptionsPluginItem("JSON", "IFileFormatPluginCapability", "json v2.0.0", id: "plugin.json", description: "Reads JSON catalogs"),
            new OptionsPluginItem("XML", "IFileFormatPluginCapability", "xml v2.0.0", id: "plugin.xml", description: "Reads XML catalogs")
        ]);

        vm.SelectedPlugin = vm.Plugins.Single(plugin => plugin.Name == "XML");

        Assert.Equal("Reads XML catalogs", vm.SelectedPluginDescription);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SelectedPluginDescription_FallsBackWhenPluginHasNoDescription(string description)
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetPlugins(
        [
            new OptionsPluginItem("JSON", "IFileFormatPluginCapability", "json v2.0.0", id: "plugin.json", description: description)
        ]);

        Assert.Equal(LocalizationService.Current.Get(UiKeys.NoPluginDescription), vm.SelectedPluginDescription);
        Assert.DoesNotContain(vm.SelectedPluginDescription, vm.InfoMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedPluginDescription_FallsBackWhenNothingIsSelected()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetPlugins([]);

        Assert.Null(vm.SelectedPlugin);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.NoPluginDescription), vm.SelectedPluginDescription);
    }

    [Fact]
    public void SetPlugins_RefreshKeepsDescriptionSeparateFromLoadedCount()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetPlugins(
        [
            new OptionsPluginItem("JSON", "IFileFormatPluginCapability", "json v2.0.0", id: "plugin.json", description: "Reads JSON catalogs"),
            new OptionsPluginItem("XML", "IFileFormatPluginCapability", "xml v2.0.0", id: "plugin.xml", description: "Reads XML catalogs")
        ]);
        vm.SelectedPlugin = vm.Plugins.Single(plugin => plugin.Name == "XML");

        vm.SetPlugins(
        [
            new OptionsPluginItem("JSON", "IFileFormatPluginCapability", "json v2.0.0", id: "plugin.json", description: "Reads JSON catalogs"),
            new OptionsPluginItem("XML", "IFileFormatPluginCapability", "xml v2.0.0", id: "plugin.xml", description: "Reads XML catalogs")
        ]);

        Assert.DoesNotContain("Reads", vm.InfoMessage, StringComparison.Ordinal);
        Assert.Equal("Reads JSON catalogs", vm.SelectedPluginDescription);
    }

    [Fact]
    public void HasPluginDescription_TrueForAPluginWithADescription()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetPlugins(
        [
            new OptionsPluginItem("JSON", "IFileFormatPluginCapability", "json v2.0.0", id: "plugin.json", description: "Reads JSON catalogs")
        ]);

        Assert.True(vm.HasPluginDescription);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void HasPluginDescription_FalseForAPluginWithoutADescription(string description)
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetPlugins(
        [
            new OptionsPluginItem("JSON", "IFileFormatPluginCapability", "json v2.0.0", id: "plugin.json", description: description)
        ]);

        Assert.False(vm.HasPluginDescription);
    }

    [Fact]
    public void HasPluginDescription_FalseWhenNothingIsSelected()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetPlugins([]);

        Assert.False(vm.HasPluginDescription);
    }

    [Fact]
    public void HasPluginDescription_NotifiesAndTracksTheSelectedPlugin()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetPlugins(
        [
            new OptionsPluginItem("JSON", "IFileFormatPluginCapability", "json v2.0.0", id: "plugin.json", description: "Reads JSON catalogs"),
            new OptionsPluginItem("XML", "IFileFormatPluginCapability", "xml v2.0.0", id: "plugin.xml")
        ]);
        var raised = new List<string>();
        vm.PropertyChanged += (_, args) => raised.Add(args.PropertyName ?? string.Empty);
        Assert.True(vm.HasPluginDescription);

        vm.SelectedPlugin = vm.Plugins.Single(plugin => plugin.Name == "XML");

        Assert.False(vm.HasPluginDescription);
        Assert.Contains(nameof(OptionsDialogViewModel.HasPluginDescription), raised);
    }
}
