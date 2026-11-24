using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using SkyCD.App.Views;
using SkyCD.Couchbase;
using SkyCD.Couchbase.Repository;
using SkyCD.Plugin.Abstractions.Capabilities.Configuration;
using SkyCD.Plugin.Metadata.AudioCD;
using SkyCD.Plugin.Metadata.EBook;
using SkyCD.Plugin.Metadata.EComic;
using SkyCD.Plugin.Metadata.Fonts;
using SkyCD.Plugin.Runtime.Configuration;
using SkyCD.Plugin.Runtime.Documents;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.Sample.Config;
using SkyCD.Presentation.ViewModels;
using Xunit;
using SkyCD.Plugin.Metadata.AudioCD.Configuration;
using SkyCD.Plugin.Metadata.AudioCD.Models;
using SkyCD.Plugin.Metadata.EBook.Configuration;
using SkyCD.Plugin.Metadata.EComic.Configuration;
using SkyCD.Plugin.Metadata.Fonts.Configuration;
using SkyCD.Plugin.Sample.Config.Configuration;

namespace SkyCD.UI.Tests;

public sealed class PluginOptionsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "SkyCD-plugin-options-" + Guid.NewGuid().ToString("N"));
    private readonly DatabaseManager database = new();
    private readonly PluginManager plugins;
    private readonly PluginConfigurationStore store;
    private readonly PluginConfigurationManager manager;
    private static string TestPluginId => typeof(OptionsTestCapability).Assembly.GetName().Name!;

    public PluginOptionsTests()
    {
        Directory.CreateDirectory(directory);
        database.Connect("default", Path.Combine(directory, "db"));
        var repositories = new RepositoryManager(database);
        store = new PluginConfigurationStore((IRepository<PluginConfigurationDocument>)repositories.For<PluginConfigurationDocument>());
        plugins = new PluginManager(NullLogger<PluginManager>.Instance,
            new AssembliesListFactory(NullLogger<AssembliesListFactory>.Instance), new DiscoveredPluginFactory(),
            (IRepository<PluginDocument>)repositories.For<PluginDocument>());
        manager = new PluginConfigurationManager(plugins, store);
        OptionsTestCapability.Reset();
        foreach (var assembly in new[] { typeof(OptionsTestCapability).Assembly, typeof(SampleConfigurationPlugin).Assembly })
            File.Copy(assembly.Location, Path.Combine(directory, Path.GetFileName(assembly.Location)));
        plugins.Discover(directory, new Version(3, 0, 0));
    }

    private OptionsDialogViewModel CreateViewModel()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetPlugins(plugins.GetPluginDescriptors().Select(plugin => new OptionsPluginItem(plugin.Name, "test", "test",
            id: plugin.Id, isEnabled: plugin.IsEnabled)));
        vm.SetPluginConfigurations(manager, TimeSpan.FromMilliseconds(100));
        return vm;
    }

    private static OptionsSettingCategory TestCategory(OptionsDialogViewModel vm) =>
        vm.SettingCategories.Single(category => category.PluginPage?.Registration.PluginId == TestPluginId);

    [AvaloniaFact]
    public void EnabledFiltering_DuplicateTitles_SelectionAndFallbackUseIds()
    {
        using var vm = CreateViewModel();
        var category = TestCategory(vm);
        Assert.Equal("Plugins", category.Title);
        Assert.Equal(2, vm.SettingCategories.Count(c => c.Title == "Plugins"));
        Assert.All(vm.SettingCategories.Take(10), c => Assert.Null(c.PluginPage));
        Assert.Equal("locations", vm.SettingCategories[6].Id);
        Assert.Equal("toolbar", vm.SettingCategories[7].Id);
        Assert.Equal("mcp", vm.SettingCategories[8].Id);
        Assert.Equal("acp", vm.SettingCategories[9].Id);
        vm.SelectedSettingCategory = category;
        Assert.False(vm.ShowPluginListSection);
        Assert.True(vm.ShowPluginSettingsSection);
        vm.SetAiDescriptionAvailable(true);
        Assert.Equal(category.Id, vm.SelectedSettingCategory?.Id);
        vm.SetAiDescriptionAvailable(false);
        Assert.Equal(category.Id, vm.SelectedSettingCategory?.Id);
        vm.SettingsSearchText = "plug";
        Assert.Contains(vm.FilteredSettingCategories, c => c.Id == category.Id);
        var plugin = vm.Plugins.Single(p => p.Id == TestPluginId);
        plugin.IsEnabled = false;
        Assert.DoesNotContain(vm.SettingCategories, c => c.Id == category.Id);
        Assert.Equal("plugins", vm.SelectedSettingCategory?.Id);
        plugin.IsEnabled = true;
        Assert.Contains(vm.SettingCategories, c => c.Id == category.Id);
        vm.RestoreSettingCategory(category.Id, 0);
        Assert.Same(category.PluginPage, vm.SelectedPluginPage);
        vm.SettingsSearchText = "no matching category";
        Assert.Empty(vm.FilteredSettingCategories);
        Assert.True(vm.ShowNoSearchResults);
        Assert.False(vm.ShowPluginSettingsSection);
    }

    [AvaloniaFact]
    public void BuiltInIndexes_KeepToolbarAndAiBeforePluginCategories()
    {
        using var vm = CreateViewModel();
        var withoutAi = vm.SettingCategories.ToList();
        Assert.Equal([0, 1, 3, 4, 6, 7, 8, 9, 2, 5, 12],
            withoutAi.Take(11).Select(category => category.BuiltInIndex));
        Assert.Equal(["plugins", "language", "statuses", "custom-properties", "scan", "details-columns",
            "locations", "toolbar", "mcp", "acp", "mcp-tools"], withoutAi.Take(11).Select(category => category.Id));
        Assert.All(withoutAi.Skip(11), category => Assert.Equal(-1, category.BuiltInIndex));

        vm.SetAiDescriptionAvailable(true);
        var categories = vm.SettingCategories.ToList();
        Assert.Equal([0, 1, 3, 4, 6, 7, 8, 9, 10, 11, 2, 5, 12],
            categories.Take(13).Select(category => category.BuiltInIndex));
        Assert.Equal("toolbar", categories[7].Id);
        Assert.Equal("ai-description", categories[8].Id);
        Assert.Equal("ai-provider", categories[9].Id);
        Assert.Equal((2, "mcp"), (categories[10].BuiltInIndex, categories[10].Id));
        Assert.Equal((5, "acp"), (categories[11].BuiltInIndex, categories[11].Id));
        Assert.Equal((12, "mcp-tools"), (categories[12].BuiltInIndex, categories[12].Id));
        Assert.All(categories.Skip(13), category =>
        {
            Assert.Equal(-1, category.BuiltInIndex);
            Assert.NotNull(category.PluginPage);
        });
    }

    [AvaloniaFact]
    public void GroupedPluginPage_RendersSingleParent_AndLeavesOtherPagesTopLevel()
    {
        OptionsTestCapability.Group = "Metadata";
        OptionsTestCapability.Title = "Audio CD";
        using var vm = CreateViewModel();
        var category = TestCategory(vm);
        Assert.Equal("Metadata", category.Group);
        Assert.Equal("Audio CD", category.Title);
        Assert.Contains(vm.FilteredSettingCategories, filtered => filtered.Id == category.Id);

        var header = Assert.Single(vm.SidebarEntries, entry => entry.IsGroupHeader && entry.Title == "Metadata");
        Assert.Equal("Metadata", header.Title);
        var headerIndex = vm.SidebarEntries.IndexOf(header);
        var children = vm.SidebarEntries.Skip(headerIndex + 1).TakeWhile(entry => !entry.IsGroupHeader).ToList();
        var child = Assert.Single(children);
        Assert.Equal(category.Id, child.Category?.Id);
        Assert.Equal(1, child.Depth);
        Assert.Contains(vm.SidebarEntries, entry =>
            entry.Depth == 0 && entry.Category?.PluginPage?.Registration.PluginId == "SkyCD.Plugin.Sample.Configuration");
    }

    [AvaloniaFact]
    public void SelectingGroupHeader_SelectsFirstChild_AndHeaderShowsFullPath()
    {
        OptionsTestCapability.Group = "Metadata";
        OptionsTestCapability.Title = "Audio CD";
        using var vm = CreateViewModel();
        var category = TestCategory(vm);
        var header = vm.SidebarEntries.Single(entry => entry.IsGroupHeader && entry.Title == "Metadata");

        vm.SelectedSidebarEntry = header;

        Assert.Equal(category.Id, vm.SelectedSettingCategory?.Id);
        Assert.False(vm.SelectedSidebarEntry!.IsGroupHeader);
        Assert.Equal("Metadata  >  Audio CD", vm.CurrentCategoryPath);
        Assert.True(vm.ShowPluginSettingsSection);
    }

    [AvaloniaFact]
    public void Search_FiltersGroupChildren_AndHidesEmptyGroups()
    {
        OptionsTestCapability.Group = "Metadata";
        OptionsTestCapability.Title = "Audio CD";
        using var vm = CreateViewModel();
        var category = TestCategory(vm);

        vm.SettingsSearchText = "Audio";
        var header = Assert.Single(vm.SidebarEntries, entry => entry.IsGroupHeader);
        Assert.Equal("Metadata", header.Title);
        var onlyEntry = Assert.Single(vm.SidebarEntries, entry => !entry.IsGroupHeader);
        Assert.Equal(category.Id, onlyEntry.Category?.Id);
        Assert.Equal(category.Id, vm.SelectedSettingCategory?.Id);

        vm.SettingsSearchText = "Sample Configuration";
        Assert.DoesNotContain(vm.SidebarEntries, entry => entry.IsGroupHeader);
        Assert.Single(vm.SidebarEntries);

        vm.SettingsSearchText = "Metadata";
        Assert.Empty(vm.SidebarEntries);
        Assert.Null(vm.SelectedSidebarEntry);
    }

    [AvaloniaFact]
    public void Sidebar_KeyboardNavigation_SkipsGroupHeaders()
    {
        OptionsTestCapability.Group = "Metadata";
        OptionsTestCapability.Title = "Audio CD";
        using var vm = CreateViewModel();
        var child = vm.SidebarEntries.Single(entry =>
            entry.Category?.PluginPage?.Registration.PluginId == TestPluginId);
        var childIndex = vm.SidebarEntries.IndexOf(child);
        Assert.True(childIndex >= 2);
        Assert.True(vm.SidebarEntries[childIndex - 1].IsGroupHeader);
        var previous = vm.SidebarEntries[childIndex - 2];

        var window = new OptionsWindow { DataContext = vm };
        window.Show();
        try
        {
            var list = window.FindControl<ListBox>("SettingsCategoriesList")!;
            // Selecting the child scrolls it into view; ScrollIntoView alone does not realize
            // off-screen containers in this headless window.
            list.SelectedIndex = childIndex;
            Dispatcher.UIThread.RunJobs();
            var headerContainer = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(childIndex - 1));
            var childContainer = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(childIndex));
            Assert.False(headerContainer.Focusable);
            Assert.True(childContainer.Focusable);

            childContainer.Focus();
            window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.False(vm.SelectedSidebarEntry!.IsGroupHeader);
            Assert.Equal(previous.Category?.Id, vm.SelectedSettingCategory?.Id);

            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(child.Category?.Id, vm.SelectedSettingCategory?.Id);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AiGroup_KeyboardNavigation_MovesBetweenChildren()
    {
        using var vm = CreateViewModel();
        vm.SetAiDescriptionAvailable(true);
        var description = vm.SidebarEntries.Single(entry => entry.Category?.Id == "ai-description");
        var provider = vm.SidebarEntries.Single(entry => entry.Category?.Id == "ai-provider");
        var descriptionIndex = vm.SidebarEntries.IndexOf(description);
        var header = vm.SidebarEntries[descriptionIndex - 1];
        Assert.True(header.IsGroupHeader);
        Assert.Equal("AI", header.Title);
        Assert.Equal(descriptionIndex + 1, vm.SidebarEntries.IndexOf(provider));

        var window = new OptionsWindow { DataContext = vm };
        window.Show();
        try
        {
            var list = window.FindControl<ListBox>("SettingsCategoriesList")!;
            list.ScrollIntoView(descriptionIndex);
            Dispatcher.UIThread.RunJobs();
            list.SelectedIndex = descriptionIndex;
            Dispatcher.UIThread.RunJobs();
            var container = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(descriptionIndex));
            container.Focus();

            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("ai-provider", vm.SelectedSettingCategory?.Id);
            Assert.False(vm.SelectedSidebarEntry!.IsGroupHeader);

            window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("ai-description", vm.SelectedSettingCategory?.Id);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DisablingOnlySearchResult_FallsBackToVisibleBuiltInCategory()
    {
        OptionsTestCapability.Title = "Unique configuration";
        using var vm = CreateViewModel();
        vm.SettingsSearchText = "Unique configuration";
        Assert.Equal(TestCategory(vm).Id, vm.SelectedSettingCategory?.Id);
        vm.Plugins.Single(p => p.Id == TestPluginId).IsEnabled = false;
        Assert.Equal("plugins", vm.SelectedSettingCategory?.Id);
        Assert.True(vm.ShowPluginListSection);
        Assert.Contains(vm.SelectedSettingCategory, vm.FilteredSettingCategories);
    }

    [AvaloniaFact]
    public void LocalizationAndPersistedIdentity_DoNotReinterpretPluginSelection()
    {
        var service = SkyCD.UI.Localization.LocalizationService.Current;
        var culture = service.CurrentCulture;
        var threadCulture = System.Globalization.CultureInfo.CurrentCulture;
        var uiCulture = System.Globalization.CultureInfo.CurrentUICulture;
        var defaultCulture = System.Globalization.CultureInfo.DefaultThreadCurrentCulture;
        var defaultUiCulture = System.Globalization.CultureInfo.DefaultThreadCurrentUICulture;
        using var vm = CreateViewModel();
        var category = TestCategory(vm);
        vm.SelectedSettingCategory = category;
        try
        {
            service.SetCulture(System.Globalization.CultureInfo.GetCultureInfo("lt"));
            Assert.Equal(category.Id, vm.SelectedSettingCategory?.Id);
            Assert.Same(category.PluginPage, vm.SelectedPluginPage);
            vm.RestoreSettingCategory("missing-plugin-page", 2);
            Assert.Equal("mcp", vm.SelectedSettingCategory?.Id);
            vm.RestoreSettingCategory(category.Id, 2);
            Assert.Same(category.PluginPage, vm.SelectedPluginPage);
        }
        finally
        {
            service.SetCulture(culture);
            System.Globalization.CultureInfo.CurrentCulture = threadCulture;
            System.Globalization.CultureInfo.CurrentUICulture = uiCulture;
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = defaultCulture;
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = defaultUiCulture;
        }
    }

    [AvaloniaFact]
    public void DuplicatePluginTitles_KeepIndependentSelectionAndSearchResults()
    {
        OptionsTestCapability.Title = "Sample Configuration";
        using var vm = CreateViewModel();
        vm.SettingsSearchText = "sample configuration";
        Assert.Equal(2, vm.FilteredSettingCategories.Count);
        Assert.Equal(2, vm.FilteredSettingCategories.Select(c => c.Id).Distinct().Count());
        foreach (var category in vm.FilteredSettingCategories.ToArray())
        {
            vm.SelectedSettingCategory = category;
            Assert.Same(category.PluginPage, vm.SelectedPluginPage);
        }
    }

    [AvaloniaFact]
    public void InitiallyDisabledPlugin_CanBeEnabledWithoutChangingRuntimeState()
    {
        plugins.SavePluginEnabledStates([(TestPluginId, false)]);
        plugins.Discover(directory, new Version(3, 0, 0));
        Assert.DoesNotContain(manager.GetRegistrations(), r => r.PluginId == TestPluginId);
        Assert.Contains(manager.GetRegistrations(true), r => r.PluginId == TestPluginId);
        using var vm = CreateViewModel();
        Assert.DoesNotContain(vm.SettingCategories, c => c.PluginPage?.Registration.PluginId == TestPluginId);
        vm.Plugins.Single(p => p.Id == TestPluginId).IsEnabled = true;
        Assert.NotNull(TestCategory(vm));
        vm.CapturePluginStates();
        vm.SetPlugins(plugins.GetPluginDescriptors().Select(plugin => new OptionsPluginItem(plugin.Name, "test", "test",
            id: plugin.Id, isEnabled: plugin.IsEnabled)));
        Assert.NotNull(TestCategory(vm));
        Assert.DoesNotContain(manager.GetRegistrations(), r => r.PluginId == TestPluginId);
    }

    [AvaloniaFact]
    public async Task RealOptionsWindow_EmbedsDraft_RetainsEdits_AndCancelDoesNotSave()
    {
        store.SaveSettings(TestPluginId, new Dictionary<string, string?> { ["value"] = "stored" });
        using var vm = CreateViewModel();
        vm.SelectedSettingCategory = TestCategory(vm);
        var window = new OptionsWindow { DataContext = vm };
        window.Show();
        try
        {
            await vm.SelectedPluginPage!.LoadAsync();
            Dispatcher.UIThread.RunJobs();
            var input = Assert.IsType<TextBox>(vm.SelectedPluginPage.Content);
            Assert.Contains(input, window.GetVisualDescendants());
            Assert.Equal("stored", input.Text);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Content?.ToString() == "Configure");
            input.Text = "draft";
            var selected = vm.SelectedSettingCategory;
            vm.SelectedTabIndex = 1;
            vm.SelectedSettingCategory = selected;
            vm.SettingsSearchText = "Language";
            vm.SettingsSearchText = string.Empty;
            vm.SelectedSettingCategory = selected;
            var plugin = vm.Plugins.Single(p => p.Id == TestPluginId);
            plugin.IsEnabled = false;
            plugin.IsEnabled = true;
            vm.SelectedSettingCategory = TestCategory(vm);
            await vm.SelectedPluginPage!.LoadAsync();
            Assert.Same(input, vm.SelectedPluginPage.Content);
            Assert.Equal("draft", input.Text);
            Assert.Equal("stored", store.GetSettings(TestPluginId)["value"]);
        }
        finally { window.Close(); }
        Assert.Equal("stored", store.GetSettings(TestPluginId)["value"]);
        Assert.Null(OptionsTestCapability.Applied);
        Assert.Equal(1, OptionsTestCapability.Disposals);
    }

    [AvaloniaFact]
    public async Task Save_ValidatesBeforeWriting_ThenRefreshesLiveConsumer()
    {
        using var vm = CreateViewModel();
        var sample = vm.SettingCategories.Single(c => c.PluginPage?.Registration.PluginId == "SkyCD.Plugin.Sample.Configuration").PluginPage!;
        await sample.LoadAsync();
        Assert.IsType<SampleConfigurationPage>(sample.Content).FindControl<TextBox>("GreetingTextBox")!.Text = "other draft";
        vm.SelectedSettingCategory = TestCategory(vm);
        await vm.SelectedPluginPage!.LoadAsync();
        var input = Assert.IsType<TextBox>(vm.SelectedPluginPage.Content);
        input.Text = "changed";
        vm.AddCustomPropertyCommand.Execute(null);
        await vm.ConfirmCommand.ExecuteAsync(null);
        Assert.False(vm.DialogAccepted);
        Assert.Empty(store.GetSettings(TestPluginId));
        vm.CustomProperties.Clear();
        OptionsTestCapability.Invalid = true;
        await vm.ConfirmCommand.ExecuteAsync(null);
        Assert.False(vm.DialogAccepted);
        Assert.True(vm.SelectedPluginPage!.HasError);
        Assert.Equal("changed", input.Text);
        Assert.Empty(store.GetSettings(TestPluginId));
        Assert.Empty(store.GetSettings("SkyCD.Plugin.Sample.Configuration"));
        OptionsTestCapability.Invalid = false;
        await vm.ConfirmCommand.ExecuteAsync(null);
        Assert.True(vm.DialogAccepted);
        Assert.Equal("changed", store.GetSettings(TestPluginId)["value"]);
        Assert.Equal("changed", OptionsTestCapability.Applied!["value"]);
        using var reopened = CreateViewModel();
        reopened.SelectedSettingCategory = TestCategory(reopened);
        await reopened.SelectedPluginPage!.LoadAsync();
        Assert.Equal("changed", Assert.IsType<TextBox>(reopened.SelectedPluginPage.Content).Text);
    }

    [AvaloniaFact]
    public async Task DisabledDraft_IsNotSaved_AndRefreshKeepsExistingDraft()
    {
        using var vm = CreateViewModel();
        var page = TestCategory(vm).PluginPage!;
        await page.LoadAsync();
        Assert.IsType<TextBox>(page.Content).Text = "unsaved";
        vm.SetPluginConfigurations(manager, TimeSpan.FromMilliseconds(100));
        Assert.Same(page, TestCategory(vm).PluginPage);
        vm.Plugins.Single(p => p.Id == TestPluginId).IsEnabled = false;
        await vm.ConfirmCommand.ExecuteAsync(null);
        Assert.True(vm.DialogAccepted);
        Assert.Empty(store.GetSettings(TestPluginId));
        Assert.Null(OptionsTestCapability.Applied);
    }

    [AvaloniaFact]
    public async Task CloseDuringSave_CancelsAcceptanceWithoutPersisting()
    {
        OptionsTestCapability.DelayAcceptance = true;
        using var vm = CreateViewModel();
        vm.SelectedSettingCategory = TestCategory(vm);
        await vm.SelectedPluginPage!.LoadAsync();
        var saving = vm.ConfirmCommand.ExecuteAsync(null);
        Assert.False(saving.IsCompleted);
        vm.Dispose();
        await saving;
        Assert.False(vm.DialogAccepted);
        Assert.Empty(store.GetSettings(TestPluginId));
        Assert.Null(OptionsTestCapability.Applied);
    }

    [AvaloniaFact]
    public async Task CloseDuringSave_ContainsThrowingCancellationCallback_AndDisposesDraft()
    {
        OptionsTestCapability.DelayAcceptance = true;
        OptionsTestCapability.ThrowOnCancellation = true;
        using var vm = CreateViewModel();
        var page = TestCategory(vm).PluginPage!;
        await page.LoadAsync();
        var saving = vm.ConfirmCommand.ExecuteAsync(null);
        Assert.False(saving.IsCompleted);
        var exception = Record.Exception(vm.Dispose);
        await saving;
        Assert.Null(exception);
        Assert.Null(page.Content);
        Assert.Equal(1, OptionsTestCapability.Disposals);
        Assert.False(vm.DialogAccepted);
        Assert.Empty(store.GetSettings(TestPluginId));
        Assert.Null(OptionsTestCapability.Applied);
    }

    [AvaloniaFact]
    public async Task UnavailableDraft_IsInline_AndDoesNotBlockSavingOtherSettings()
    {
        OptionsTestCapability.FailCreation = true;
        using var vm = CreateViewModel();
        vm.SelectedSettingCategory = TestCategory(vm);
        await vm.SelectedPluginPage!.LoadAsync();
        Assert.True(vm.SelectedPluginPage.HasError);
        vm.AddCustomPropertyCommand.Execute(null);
        vm.CustomProperties[0].Name = "Edited";
        await vm.ConfirmCommand.ExecuteAsync(null);
        Assert.True(vm.DialogAccepted);
        Assert.Contains(vm.GetCustomProperties(), property => property.Name == "Edited");
        Assert.Empty(store.GetSettings(TestPluginId));
        Assert.Null(OptionsTestCapability.Applied);
    }

    [AvaloniaFact]
    public async Task NonEmbeddableDraft_ShowsInlineError_IsDisposed_AndDoesNotBlockSave()
    {
        OptionsTestCapability.NonEmbeddableContent = true;
        using var vm = CreateViewModel();
        vm.SelectedSettingCategory = TestCategory(vm);
        await vm.SelectedPluginPage!.LoadAsync();
        Assert.True(vm.SelectedPluginPage.HasError);
        Assert.Null(vm.SelectedPluginPage.Content);
        Assert.Equal(1, OptionsTestCapability.Disposals);
        await vm.ConfirmCommand.ExecuteAsync(null);
        Assert.True(vm.DialogAccepted);
        Assert.Empty(store.GetSettings(TestPluginId));
    }

    [AvaloniaFact]
    public async Task AcceptTimeout_IsInline_AndBlocksSave()
    {
        OptionsTestCapability.DelayAcceptance = true;
        using var vm = CreateViewModel();
        vm.SelectedSettingCategory = TestCategory(vm);
        await vm.SelectedPluginPage!.LoadAsync();
        await vm.ConfirmCommand.ExecuteAsync(null);
        Assert.False(vm.DialogAccepted);
        Assert.Contains("timed out", vm.SelectedPluginPage.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(store.GetSettings(TestPluginId));
    }

    [AvaloniaFact]
    public async Task FailedLoad_IsRetriedWhenPageIsReselected()
    {
        OptionsTestCapability.FailCreation = true;
        using var vm = CreateViewModel();
        vm.SelectedSettingCategory = TestCategory(vm);
        var page = vm.SelectedPluginPage!;
        await page.LoadAsync();
        Assert.True(page.HasError);
        Assert.Null(page.Content);

        OptionsTestCapability.FailCreation = false;
        vm.SelectedSettingCategory = vm.SettingCategories.First(category => category.Id == "language");
        vm.SelectedSettingCategory = TestCategory(vm);
        await vm.SelectedPluginPage!.LoadAsync();

        Assert.NotNull(vm.SelectedPluginPage.Content);
        Assert.False(vm.SelectedPluginPage.HasError);
    }

    [AvaloniaFact]
    public async Task TimedOutLoad_IsRetried_InsteadOfLatched()
    {
        OptionsTestCapability.DelayCreation = true;
        using var vm = CreateViewModel();
        var page = TestCategory(vm).PluginPage!;
        await page.LoadAsync();
        Assert.True(page.HasError);
        Assert.Contains("timed out", page.Error, StringComparison.OrdinalIgnoreCase);

        OptionsTestCapability.DelayCreation = false;
        await page.LoadAsync();

        Assert.NotNull(page.Content);
        Assert.False(page.HasError);
    }

    [AvaloniaFact]
    public async Task EveryMigratedPlugin_ProvidesControlAndRoundTripsEditedValues()
    {
        (IPluginConfigurationCapability Capability, string Control, string Value, string Key)[] cases =
        [
            (new SampleConfigurationPlugin(), "GreetingTextBox", "Hello edited", "greeting"),
            (new EBookConfigurationPlugin(), "ExtensionsTextBox", ".epub", "extensions"),
            (new EComicConfigurationPlugin(), "ExtensionsTextBox", ".cbz", "extensions"),
            (new FontConfigurationPlugin(), "PreviewTextBox", "Font edited", "previewText")
        ];
        foreach (var (capability, controlName, value, key) in cases)
        {
            using var page = await capability.CreatePageAsync(new PluginConfigurationRequest { ConfigurationId = capability.Configuration.ConfigurationId });
            var control = Assert.IsAssignableFrom<UserControl>(page.Content);
            control.FindControl<TextBox>(controlName)!.Text = value;
            var result = await page.AcceptAsync();
            Assert.True(result.Success);
            Assert.Equal(value, result.Settings[key]);
        }
        var audio = new AudioCdConfigurationPlugin();
        using var audioPage = await audio.CreatePageAsync(new PluginConfigurationRequest { ConfigurationId = audio.Configuration.ConfigurationId });
        Assert.IsAssignableFrom<UserControl>(audioPage.Content).FindControl<CheckBox>("OfflineCheckBox")!.IsChecked = true;
        Assert.True(AudioCdSettings.Parse((await audioPage.AcceptAsync()).Settings).Offline);
    }

    public void Dispose()
    {
        database.GetDatabase().Dispose();
        database.Dispose();
        Directory.Delete(directory, recursive: true);
    }
}

public sealed class OptionsTestCapability : IPluginConfigurationCapability, IPluginSettingsConsumer
{
    public static bool Invalid { get; set; }
    public static bool FailCreation { get; set; }
    public static bool DelayCreation { get; set; }
    public static bool DelayAcceptance { get; set; }
    public static bool ThrowOnCancellation { get; set; }
    public static bool NonEmbeddableContent { get; set; }
    public static int Disposals { get; private set; }
    public static IReadOnlyDictionary<string, string?>? Applied { get; private set; }
    public static string Title { get; set; } = "Plugins";
    public static string? Group { get; set; }
    public PluginConfigurationDescriptor Configuration => new("options.test", Title, Group);

    public static void Reset()
    {
        Invalid = FailCreation = DelayCreation = DelayAcceptance = ThrowOnCancellation = NonEmbeddableContent = false;
        Title = "Plugins";
        Group = null;
        Disposals = 0;
        Applied = null;
    }

    public async Task<PluginConfigurationPage> CreatePageAsync(PluginConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (FailCreation) throw new InvalidOperationException("Deliberate test creation failure");
        if (DelayCreation) await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        TextBox? input = null;
        object CreateContent()
        {
            if (NonEmbeddableContent) return "not an Avalonia control";
            return input = new TextBox { Text = request.CurrentSettings.GetValueOrDefault("value") ?? "initial" };
        }

        return new PluginConfigurationPage(
            CreateContent,
            async token =>
            {
                using var registration = ThrowOnCancellation
                    ? token.Register(() => throw new InvalidOperationException("Deliberate test cancellation failure"))
                    : default;
                if (DelayAcceptance) await Task.Delay(TimeSpan.FromSeconds(10), token);
                return new PluginConfigurationResult
                {
                    Success = !Invalid,
                    Error = Invalid ? "Invalid test draft" : null,
                    Settings = new Dictionary<string, string?> { ["value"] = input?.Text ?? "initial" }
                };
            }, () => Disposals++);
    }

    public void ApplySettings(IReadOnlyDictionary<string, string?> settings) => Applied = settings;
}
