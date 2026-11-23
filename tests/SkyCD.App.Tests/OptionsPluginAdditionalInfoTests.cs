using System;
using System.Diagnostics;
using System.IO;
using SkyCD.Formatting;
using SkyCD.Plugin.Runtime.Marketplace.Models;
using SkyCD.Presentation.Marketplace;
using SkyCD.Presentation.Plugins;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #996: the per-plugin Additional Info tab surfaces links and full metadata. The view model hides
/// rows without values, and plugin-supplied links may only launch http(s) URLs.
/// </summary>
public sealed class OptionsPluginAdditionalInfoTests
{
    [Theory]
    [InlineData("https://example.com/plugins/acme", true)]
    [InlineData("http://example.com/plugins/acme", true)]
    [InlineData("HTTPS://EXAMPLE.COM/plugins/acme", true)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("\\\\server\\share\\plugin.dll", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("mailto:author@example.com", false)]
    [InlineData("ftp://example.com/plugin", false)]
    [InlineData("plugins/acme", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsLaunchable_AllowsOnlyAbsoluteHttpAndHttps(string? value, bool expected)
    {
        Assert.Equal(expected, ExternalLinkLauncher.IsLaunchable(value));
    }

    [Fact]
    public void TryOpen_DoesNotInvokeTheShellForANonHttpScheme()
    {
        ProcessStartInfo? started = null;

        var opened = ExternalLinkLauncher.TryOpen(
            "file:///C:/Windows/System32/calc.exe",
            startInfo => started = startInfo);

        Assert.False(opened);
        Assert.Null(started);
    }

    [Fact]
    public void TryOpen_LaunchesHttpUrlsThroughTheShell()
    {
        ProcessStartInfo? started = null;

        var opened = ExternalLinkLauncher.TryOpen("https://example.com/acme", startInfo => started = startInfo);

        Assert.True(opened);
        Assert.NotNull(started);
        Assert.Equal("https://example.com/acme", started!.FileName);
        Assert.True(started.UseShellExecute);
    }

    [Fact]
    public void TryOpen_ReportsShellFailuresWithoutThrowing()
    {
        Assert.False(ExternalLinkLauncher.TryOpen("https://example.com/acme", _ =>
            throw new InvalidOperationException("no default browser")));
    }

    [Fact]
    public void AdditionalInfo_MapsEveryAvailableField()
    {
        var vm = CreateViewModel();
        vm.SetPlugins(
        [
            new OptionsPluginItem(
                "XML catalogs",
                "Acme Corp",
                "acme.xml v2.0",
                id: "acme.xml",
                authorUrl: "https://acme.example/author",
                description: "Reads XML catalogs",
                primaryExtension: "xml",
                projectUrl: "https://acme.example/xml",
                version: "2.0",
                capabilityType: "XmlCatalogFileFormatPlugin",
                sizeBytes: 1536,
                updateSource: PluginUpdateSource.Marketplace)
        ]);

        Assert.Equal("Acme Corp", vm.SelectedPluginDetailsType);
        Assert.True(vm.HasSelectedPluginDetailsType);
        Assert.Equal("2.0", vm.SelectedPluginDetailsVersionNumber);
        Assert.True(vm.HasSelectedPluginDetailsVersionNumber);
        Assert.Equal(SizeFormatting.FormatBytes(1536), vm.SelectedPluginDetailsSize);
        Assert.True(vm.HasSelectedPluginDetailsSize);
        Assert.Equal("acme.xml", vm.SelectedPluginDetailsId);
        Assert.Equal("XmlCatalogFileFormatPlugin", vm.SelectedPluginDetailsCapabilityType);
        Assert.True(vm.HasSelectedPluginDetailsCapabilityType);
        Assert.Equal("xml", vm.SelectedPluginDetailsExtension);
        Assert.True(vm.HasSelectedPluginDetailsExtension);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.PluginUpdatesFromMarketplace),
            vm.SelectedPluginDetailsUpdateSource);
        Assert.True(vm.HasSelectedPluginDetailsUpdateSource);
        Assert.True(vm.HasSelectedPluginDetailsProjectUrl);
        Assert.True(vm.HasSelectedPluginDetailsAuthorUrl);
    }

    [Fact]
    public void AdditionalInfo_HidesRowsWithoutValues()
    {
        var vm = CreateViewModel();
        vm.SetPlugins([new OptionsPluginItem("Accounts", "SkyCD", "acme.accounts v1.0", id: "acme.accounts")]);

        Assert.Equal(string.Empty, vm.SelectedPluginDetailsVersionNumber);
        Assert.False(vm.HasSelectedPluginDetailsVersionNumber);
        Assert.Equal(string.Empty, vm.SelectedPluginDetailsSize);
        Assert.False(vm.HasSelectedPluginDetailsSize);
        Assert.False(vm.HasSelectedPluginDetailsCapabilityType);
        Assert.Equal(string.Empty, vm.SelectedPluginDetailsExtension);
        Assert.False(vm.HasSelectedPluginDetailsExtension);
        Assert.False(vm.HasSelectedPluginDetailsUpdateSource);
        Assert.False(vm.HasSelectedPluginDetailsProjectUrl);
        Assert.False(vm.HasSelectedPluginDetailsAuthorUrl);
        // The identifier and vendor always have a value.
        Assert.True(vm.HasSelectedPluginDetailsType);
        Assert.Equal("acme.accounts", vm.SelectedPluginDetailsId);
    }

    [Fact]
    public void AdditionalInfo_LocalizesThePluginFolderUpdateSource()
    {
        var vm = CreateViewModel();
        vm.SetPlugins(
        [
            new OptionsPluginItem("XML", "Acme Corp", "acme.xml v2.0", id: "acme.xml",
                updateSource: PluginUpdateSource.PluginFolder)
        ]);

        Assert.True(vm.HasSelectedPluginDetailsUpdateSource);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.PluginUpdatesFromPluginFolder),
            vm.SelectedPluginDetailsUpdateSource);
    }

    [Fact]
    public void AdditionalInfo_HidesAndBlocksLinksWithANonHttpScheme()
    {
        var vm = CreateViewModel();
        vm.SetPlugins(
        [
            new OptionsPluginItem("XML", "Acme Corp", "acme.xml v2.0", id: "acme.xml",
                authorUrl: "file:///C:/Windows/System32/calc.exe",
                projectUrl: "\\\\attacker\\share\\plugin.exe")
        ]);

        Assert.False(vm.HasSelectedPluginDetailsAuthorUrl);
        Assert.False(vm.HasSelectedPluginDetailsProjectUrl);
        Assert.False(vm.OpenSelectedPluginAuthorUrlCommand.CanExecute(null));
        Assert.False(vm.OpenSelectedPluginProjectUrlCommand.CanExecute(null));

        // Executing the guarded command must stay a no-op rather than reaching the shell.
        vm.OpenSelectedPluginAuthorUrlCommand.Execute(null);
    }

    [Fact]
    public void AdditionalInfo_EnablesLinksForHttpUrls()
    {
        var vm = CreateViewModel();
        vm.SetPlugins(
        [
            new OptionsPluginItem("XML", "Acme Corp", "acme.xml v2.0", id: "acme.xml",
                authorUrl: "https://acme.example/author",
                projectUrl: "http://acme.example/xml")
        ]);

        Assert.True(vm.HasSelectedPluginDetailsAuthorUrl);
        Assert.True(vm.HasSelectedPluginDetailsProjectUrl);
        Assert.True(vm.OpenSelectedPluginAuthorUrlCommand.CanExecute(null));
        Assert.True(vm.OpenSelectedPluginProjectUrlCommand.CanExecute(null));
    }

    [Fact]
    public void MarketplaceAdditionalInfo_ShowsTheProjectLinkOnlyForHttpUrls()
    {
        var vm = CreateViewModel();

        vm.SelectedMarketplacePlugin = CreateMarketplaceItem(new Uri("https://acme.example/xml"));
        Assert.True(vm.HasSelectedMarketplaceProjectUrl);
        Assert.True(vm.OpenSelectedMarketplaceProjectUrlCommand.CanExecute(null));

        vm.SelectedMarketplacePlugin = CreateMarketplaceItem(new Uri("file:///C:/Windows/System32/calc.exe"));
        Assert.False(vm.HasSelectedMarketplaceProjectUrl);
        Assert.False(vm.OpenSelectedMarketplaceProjectUrlCommand.CanExecute(null));

        vm.SelectedMarketplacePlugin = CreateMarketplaceItem(projectUrl: null);
        Assert.False(vm.HasSelectedMarketplaceProjectUrl);
    }

    [Fact]
    public void PluginFolderSize_SumsNestedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "skycd-plugin-size-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "nested");
        Directory.CreateDirectory(nested);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "plugin.dll"), new byte[100]);
            File.WriteAllBytes(Path.Combine(nested, "dependency.dll"), new byte[24]);

            Assert.Equal(124L, PluginFolderSize.TryComputeBytes(Path.Combine(root, "plugin.dll")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PluginFolderSize_ReturnsNullWhenNoFolderIsKnown()
    {
        Assert.Null(PluginFolderSize.TryComputeBytes(null));
        Assert.Null(PluginFolderSize.TryComputeBytes(string.Empty));
        Assert.Null(PluginFolderSize.TryComputeBytes(
            Path.Combine(Path.GetTempPath(), "skycd-missing-" + Guid.NewGuid().ToString("N"), "plugin.dll")));
    }

    private static OptionsDialogViewModel CreateViewModel() => new(["English"]);

    private static MarketplacePluginItem CreateMarketplaceItem(Uri? projectUrl) => new(
        new MarketplacePackage
        {
            SourceUri = new Uri("https://feed.example/index.json"),
            PackageId = "acme.xml",
            ProjectUrl = projectUrl
        });
}
