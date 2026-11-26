using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using SkyCD.Presentation.ViewModels;
using SkyCD.SDK;
using SkyCD.SDK.Capabilities.Options;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class OptionsDialogViewModelOptionsPagesTests
{
    private const string PluginId = "skycd.locations";

    [Fact]
    public void ContributedPage_SitsAfterCollectionsAndBeforeScan()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetCollections(new CollectionsDialogViewModel());
        viewModel.SetPlugins([EnabledPlugin()]);
        var capability = new TestCapability("locations", "Locations");
        viewModel.SetPluginOptionsPages([Provide(capability)]);

        var ids = viewModel.SettingCategories.Select(category => category.Id).ToArray();
        var collections = Array.IndexOf(ids, OptionsDialogViewModel.CollectionsCategoryId);
        var page = Array.IndexOf(ids, PageId(capability));
        Assert.True(page > collections);
        Assert.Equal(3 + 1 + 1 + 1, page); // plugins, language, statuses, custom-properties, collections, accounts, page, scan
        Assert.Equal("scan", ids[page + 1]);
    }

    [Fact]
    public void ContributedPage_FollowsPluginEnabledState()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);
        var plugin = EnabledPlugin();
        viewModel.SetPlugins([plugin]);
        viewModel.SetPluginOptionsPages([Provide(new TestCapability("locations", "Locations"))]);

        Assert.Contains(viewModel.SettingCategories, category => category.Id.StartsWith("options:", StringComparison.Ordinal));

        plugin.IsEnabled = false;

        Assert.DoesNotContain(viewModel.SettingCategories, category => category.Id.StartsWith("options:", StringComparison.Ordinal));

        plugin.IsEnabled = true;

        Assert.Contains(viewModel.SettingCategories, category => category.Id.StartsWith("options:", StringComparison.Ordinal));
    }

    [Fact]
    public void SelectingContributedPage_ShowsItsSectionAndCountsAsContent()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetPlugins([EnabledPlugin()]);
        var capability = new TestCapability("locations", "Locations");
        viewModel.SetPluginOptionsPages([Provide(capability)]);
        viewModel.SelectedSettingCategory = viewModel.SettingCategories.Single(category => category.Id == PageId(capability));

        Assert.True(viewModel.ShowContributedOptionsPageSection);
        Assert.True(viewModel.HasVisibleCategoryContent);
        Assert.False(viewModel.ShowNoSearchResults);
        Assert.Equal("Locations", viewModel.SelectedContributedPage?.Title);
    }

    [Fact]
    public void Search_FindsContributedPageByItsTranslatedTitle()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetPlugins([EnabledPlugin()]);
        var capability = new TestCapability("locations", "Locations");
        viewModel.SetPluginOptionsPages([Provide(capability)]);

        viewModel.SettingsSearchText = "Locations";

        Assert.Contains(viewModel.FilteredSettingCategories, category => category.Id == PageId(capability));
        Assert.Equal(PageId(capability), viewModel.SelectedSettingCategory?.Id);
        Assert.True(viewModel.ShowContributedOptionsPageSection);
    }

    [Fact]
    public void EnsureContent_CreatesTheViewOnceAndDisposesItWithTheDialog()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetPlugins([EnabledPlugin()]);
        var content = new DisposableControl();
        var capability = new TestCapability("locations", "Locations", () => content);
        viewModel.SetPluginOptionsPages([Provide(capability)]);
        viewModel.SelectedSettingCategory = viewModel.SettingCategories.Single(category => category.Id == PageId(capability));
        var page = viewModel.SelectedContributedPage!;

        Assert.Null(page.Content);
        page.EnsureContent();
        page.EnsureContent();

        Assert.Equal(1, capability.CreateCount);
        Assert.Same(content, page.Content);
        Assert.True(page.HasContent);

        viewModel.Dispose();

        Assert.True(content.Disposed);
    }

    [Fact]
    public void NonEmbeddableContent_ReportsAnInlineError()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetPlugins([EnabledPlugin()]);
        var capability = new TestCapability("locations", "Locations", () => null!);
        viewModel.SetPluginOptionsPages([Provide(capability)]);
        viewModel.SelectedSettingCategory = viewModel.SettingCategories.Single(category => category.Id == PageId(capability));
        var page = viewModel.SelectedContributedPage!;

        page.EnsureContent();

        Assert.True(page.HasError);
        Assert.False(page.HasContent);
        Assert.Contains("Locations", page.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreSettingCategory_ResolvesContributedPageById()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetPlugins([EnabledPlugin()]);
        var capability = new TestCapability("locations", "Locations");
        viewModel.SetPluginOptionsPages([Provide(capability)]);

        viewModel.RestoreSettingCategory(PageId(capability), legacyTabIndex: 0);

        Assert.Equal(PageId(capability), viewModel.SelectedSettingCategory?.Id);
        Assert.Equal(PageId(capability), viewModel.SelectedSidebarEntry?.Category?.Id);
    }

    [Fact]
    public void Title_IsReadFromThePluginOnEveryAccess()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetPlugins([EnabledPlugin()]);
        var capability = new TestCapability("locations", "Locations");
        viewModel.SetPluginOptionsPages([Provide(capability)]);
        viewModel.SelectedSettingCategory = viewModel.SettingCategories.Single(category => category.Id == PageId(capability));
        var page = viewModel.SelectedContributedPage!;

        Assert.Equal("Locations", page.Title);

        capability.Title = "Vietos";

        Assert.Equal("Vietos", page.Title);
    }

    [Fact]
    public void EnsureContent_FailureShowsInlineErrorWithoutGlobalReport()
    {
        using var _ = ObserveFailures(out var reported);
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetPlugins([EnabledPlugin()]);
        var capability = new FlakyCapability("locations", "Locations")
        {
            ContentSource = () => throw new InvalidOperationException("page failed")
        };
        viewModel.SetPluginOptionsPages([ProvideFlaky(capability)]);
        viewModel.SelectedSettingCategory = viewModel.SettingCategories.Single(category => category.Id == FlakyPageId(capability));
        var page = viewModel.SelectedContributedPage!;

        page.EnsureContent();

        Assert.True(page.HasError);
        Assert.False(page.HasContent);
        Assert.Contains("Locations", page.Error, StringComparison.Ordinal);
        Assert.Empty(reported);
    }

    [Fact]
    public void Title_FailureFallsBackAndReportsWithCapabilityAttribution()
    {
        using var _ = ObserveFailures(out var reported);
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetPlugins([EnabledPlugin()]);
        var capability = new FlakyCapability("locations", "Locations");
        viewModel.SetPluginOptionsPages([ProvideFlaky(capability)]);
        viewModel.SelectedSettingCategory = viewModel.SettingCategories.Single(category => category.Id == FlakyPageId(capability));
        var page = viewModel.SelectedContributedPage!;

        capability.FailTitle = true;

        Assert.Equal("Locations", page.Title);

        var failure = Assert.Single(reported);
        Assert.Equal("options", failure.Operation);
        Assert.Equal(IdentityOf(capability.GetType()), failure.PluginIdentity);
        Assert.NotEqual(IdentityOf(typeof(OptionsContributedPage)), failure.PluginIdentity);
    }

    [Fact]
    public void RemovingProvider_DisposesTheRetiredPage()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetPlugins([EnabledPlugin()]);
        var content = new DisposableControl();
        var capability = new TestCapability("locations", "Locations", () => content);
        viewModel.SetPluginOptionsPages([Provide(capability)]);
        viewModel.SelectedSettingCategory = viewModel.SettingCategories.Single(category => category.Id == PageId(capability));
        var page = viewModel.SelectedContributedPage!;
        page.EnsureContent();

        viewModel.SetPluginOptionsPages([]);

        Assert.True(content.Disposed);
        Assert.DoesNotContain(viewModel.SettingCategories, category => category.Id == page.Id);
    }

    private static OptionsPluginItem EnabledPlugin() =>
        new("Locations", "test", "test", id: PluginId, isEnabled: true);

    private static OptionsPageProvider Provide(TestCapability capability) =>
        new(PluginId, "Locations", capability);

    private static string PageId(TestCapability capability) => $"options:{PluginId}/{capability.PageIdValue}";

    private static OptionsPageProvider ProvideFlaky(FlakyCapability capability) =>
        new(PluginId, "Locations", capability);

    private static string FlakyPageId(FlakyCapability capability) => $"options:{PluginId}/{capability.PageIdValue}";

    private static IDisposable ObserveFailures(out List<PluginFailure> reported)
    {
        var failures = new List<PluginFailure>();
        reported = failures;
        void Capture(PluginFailure failure) => failures.Add(failure);
        PluginFailure.Reported += Capture;
        return new FailureUnsubscriber(() => PluginFailure.Reported -= Capture);
    }

    private sealed class FailureUnsubscriber(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }

    private static string IdentityOf(Type type) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(type.Assembly.GetName().Name ?? "unknown")));

    private sealed class FlakyCapability(string pageIdValue, string title) : IOptionsPageCapability
    {
        public string PageIdValue { get; } = pageIdValue;

        public bool FailTitle { get; set; }

        public Func<object?>? ContentSource { get; set; }

        public OptionsPageDescriptor OptionsPage =>
            FailTitle ? throw new InvalidOperationException("title failed") : new(PageIdValue, title, Order: 0);

        public object CreateOptionsPage() =>
            ContentSource?.Invoke() ?? throw new InvalidOperationException("no content source");
    }

    private sealed class TestCapability(string pageIdValue, string title, Func<object?>? create = null)
        : IOptionsPageCapability
    {
        public string PageIdValue { get; } = pageIdValue;
        public string Title { get; set; } = title;
        public int CreateCount { get; private set; }
        public OptionsPageDescriptor OptionsPage => new(PageIdValue, Title, Order: 0);
        public object CreateOptionsPage()
        {
            CreateCount++;
            return (create ?? (() => new Border()))()!;
        }
    }

    private sealed class DisposableControl : Border, IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
