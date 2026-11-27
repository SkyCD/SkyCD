using System;
using System.Collections.Generic;
using System.Diagnostics;
using SkyCD.Presentation.Plugins;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #1219: both Help menu links go through the shared validated launcher and surface failures.
/// Issue #1282: the success-path doubles capture the shell StartInfo instead of invoking the
/// production launcher, so the suite no longer opens real browser windows.
/// </summary>
public sealed class HelpExternalLinksTests
{
    [Fact]
    public void OpenProjectWebsite_LaunchesSourceForgeUrlThroughTheShell()
    {
        using var viewModel = MainWindowViewModel.CreateForInMemoryCatalog([]);
        ProcessStartInfo? started = null;
        viewModel.ExternalLinkOpener = (url, _) => ExternalLinkLauncher.TryOpen(
            url,
            startInfo => started = startInfo);

        viewModel.OpenProjectWebsiteCommand.Execute(null);

        Assert.Equal(MainWindowViewModel.ProjectWebsiteUrl, started?.FileName);
        Assert.True(started?.UseShellExecute);
        Assert.Equal(
            LocalizationService.Current.Get(TextKeys.OpeningSourceForgeProjectWebsite),
            viewModel.StatusText);
    }

    [Fact]
    public void OpenGithubArea_LaunchesGitHubUrlThroughTheShell()
    {
        using var viewModel = MainWindowViewModel.CreateForInMemoryCatalog([]);
        ProcessStartInfo? started = null;
        viewModel.ExternalLinkOpener = (url, _) => ExternalLinkLauncher.TryOpen(
            url,
            startInfo => started = startInfo);

        viewModel.OpenGithubAreaCommand.Execute(null);

        Assert.Equal(MainWindowViewModel.ProjectAreaUrl, started?.FileName);
        Assert.True(started?.UseShellExecute);
        Assert.Equal(
            LocalizationService.Current.Get(TextKeys.OpeningGitHubProjectArea),
            viewModel.StatusText);
    }

    [Fact]
    public void OpenProjectWebsite_FailedLaunch_ShowsLocalizedFailureAndLogs()
    {
        using var viewModel = MainWindowViewModel.CreateForInMemoryCatalog([]);
        viewModel.ExternalLinkOpener = (_, _) => false;
        var logged = new List<string>();
        viewModel.ExternalLinkFailureLog = logged.Add;

        viewModel.OpenProjectWebsiteCommand.Execute(null);

        Assert.Equal(
            LocalizationService.Current.Format(
                TextKeys.FailedToOpenSourceForgeWebsite, MainWindowViewModel.ProjectWebsiteUrl),
            viewModel.StatusText);
        Assert.Single(logged);
    }

    [Fact]
    public void OpenGithubArea_ThrowingLauncher_DoesNotCrashAndLogs()
    {
        using var viewModel = MainWindowViewModel.CreateForInMemoryCatalog([]);
        viewModel.ExternalLinkOpener = (_, _) => throw new InvalidOperationException("no default browser");
        var logged = new List<string>();
        viewModel.ExternalLinkFailureLog = logged.Add;

        viewModel.OpenGithubAreaCommand.Execute(null);

        Assert.Equal(
            LocalizationService.Current.Format(
                TextKeys.FailedToOpenGitHubArea, MainWindowViewModel.ProjectAreaUrl),
            viewModel.StatusText);
        Assert.NotEmpty(logged);
    }
}
