using System;
using System.Globalization;
using System.Linq;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #1147: the System info tab shows a diagnostics dashboard built from cheap, read-only
/// host facts. Missing values must be reported as unavailable or omitted, never fabricated.
/// </summary>
public sealed class AboutDiagnosticsViewModelTests
{
    [Fact]
    public void DiagnosticGroups_ExposeBuildRuntimeProcessAndApplication()
    {
        var vm = Create();

        Assert.Equal(LocalizationService.Current.Get(UiKeys.SystemInfoBuild), vm.BuildGroup.Title);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.SystemInfoRuntime), vm.RuntimeGroup.Title);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.SystemInfoProcess), vm.ProcessGroup.Title);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.SystemInfoApplication), vm.ApplicationGroup.Title);
    }

    [Fact]
    public void BuildGroup_ShowsInformationalAndHostVersions()
    {
        var vm = Create(new AboutApplicationSnapshot(HostVersion: "3.0.0.0"));

        Assert.Equal("3.0.0+test", Fact(vm.BuildGroup, UiKeys.SystemInfoVersion));
        Assert.Equal("3.0.0.0", Fact(vm.BuildGroup, UiKeys.SystemInfoHostVersion));
    }

    [Fact]
    public void BuildGroup_ShowsUnavailableWhenHostVersionIsMissing()
    {
        var vm = Create();

        Assert.Equal(
            LocalizationService.Current.Get(UiKeys.SystemInfoUnavailable),
            Fact(vm.BuildGroup, UiKeys.SystemInfoHostVersion));
    }

    [Fact]
    public void RuntimeGroup_ReportsRuntimeEnvironmentFacts()
    {
        var vm = Create();

        Assert.NotEmpty(Fact(vm.RuntimeGroup, UiKeys.SystemInfoDotnet));
        Assert.NotEmpty(Fact(vm.RuntimeGroup, UiKeys.SystemInfoOs));
        Assert.NotEmpty(Fact(vm.RuntimeGroup, UiKeys.SystemInfoArchitecture));
        Assert.Equal(Environment.ProcessorCount.ToString(CultureInfo.CurrentCulture),
            Fact(vm.RuntimeGroup, UiKeys.SystemInfoProcessors));
    }

    [Fact]
    public void ProcessGroup_ReportsMemoryHandlesGcAndUptime()
    {
        var vm = Create();
        var facts = vm.ProcessGroup.Facts;

        foreach (var key in new[]
                 {
                     UiKeys.SystemInfoWorkingSet,
                     UiKeys.SystemInfoManagedHeap,
                     UiKeys.SystemInfoHandles,
                     UiKeys.SystemInfoGcCollections,
                     UiKeys.Uptime
                 })
        {
            Assert.NotEmpty(Fact(vm.ProcessGroup, key));
        }

        Assert.Contains(" / ", Fact(vm.ProcessGroup, UiKeys.SystemInfoHandles), StringComparison.Ordinal);
        Assert.Equal(5, facts.Count);
    }

    [Fact]
    public void ApplicationGroup_IncludesLoadedAssembliesAndPluginCounts()
    {
        var vm = Create(new AboutApplicationSnapshot(LoadedPluginCount: 4, EnabledPluginCount: 3));

        Assert.Equal("1", Fact(vm.ApplicationGroup, UiKeys.LoadedAssemblies));
        Assert.Equal("4 / 3", Fact(vm.ApplicationGroup, UiKeys.SystemInfoPlugins));
    }

    [Fact]
    public void ApplicationGroup_OmitsPluginCountsWhenOnlyOneCountIsKnown()
    {
        var vm = Create(new AboutApplicationSnapshot(LoadedPluginCount: 4));

        Assert.DoesNotContain(vm.ApplicationGroup.Facts,
            fact => fact.Label == LocalizationService.Current.Get(UiKeys.SystemInfoPlugins));
    }

    [Fact]
    public void ApplicationGroup_IncludesCatalogNameAndEntryCountWhenOpen()
    {
        var vm = Create(new AboutApplicationSnapshot(CatalogName: "media.scd", CatalogEntryCount: 1234));

        Assert.Equal("media.scd", Fact(vm.ApplicationGroup, UiKeys.SystemInfoCatalog));
        Assert.Equal(1234.ToString("N0", CultureInfo.CurrentCulture),
            Fact(vm.ApplicationGroup, UiKeys.SystemInfoCatalogEntries));
    }

    [Fact]
    public void ApplicationGroup_UsesUntitledNameForUnnamedCatalog()
    {
        var vm = Create(new AboutApplicationSnapshot(CatalogEntryCount: 0));

        Assert.Equal(LocalizationService.Current.Get(UiKeys.SystemInfoCatalogUntitled),
            Fact(vm.ApplicationGroup, UiKeys.SystemInfoCatalog));
    }

    [Fact]
    public void ApplicationGroup_OmitsCatalogFactsWhenNoCatalogIsOpen()
    {
        var vm = Create();

        Assert.DoesNotContain(vm.ApplicationGroup.Facts,
            fact => fact.Label == LocalizationService.Current.Get(UiKeys.SystemInfoCatalog));
        Assert.DoesNotContain(vm.ApplicationGroup.Facts,
            fact => fact.Label == LocalizationService.Current.Get(UiKeys.SystemInfoCatalogEntries));
    }

    [Fact]
    public void SystemInfoKeys_AreTranslatedInBothShippedLanguagePacks()
    {
        var service = new LocalizationService();
        var keys = typeof(UiKeys).GetFields()
            .Where(field => field.IsLiteral && field.Name.StartsWith("SystemInfo", StringComparison.Ordinal))
            .Select(field => (string)field.GetRawConstantValue()!);

        foreach (var key in keys)
        {
            var english = service.Get(key, CultureInfo.GetCultureInfo("en"));
            var lithuanian = service.Get(key, CultureInfo.GetCultureInfo("lt"));
            Assert.NotEqual(key, english);
            Assert.NotEqual(key, lithuanian);
        }
    }

    private static AboutDialogViewModel Create(AboutApplicationSnapshot? snapshot = null) =>
        new(
            "SkyCD",
            "3.0.0+test",
            "https://github.com/SkyCD/SkyCD",
            loadedAssemblies: [typeof(string).Assembly],
            baseDirectory: AppContext.BaseDirectory,
            applicationSnapshot: snapshot);

    private static string Fact(AboutDiagnosticGroup group, string key)
    {
        var label = LocalizationService.Current.Get(key);
        return Assert.Single(group.Facts, fact => fact.Label == label).Value;
    }
}
