using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkyCD.Plugin.Feature.GenDesc.Localization;
using SkyCD.Plugin.Feature.GenDesc.Models;
using SkyCD.Plugin.Feature.GenDesc.ViewModels;
using SkyCD.SDK.Capabilities.GenDesc;
using SkyCD.SDK.Capabilities.Menu;
using SkyCD.TestSupport;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.Plugin.Feature.GenDesc.Tests;

public class GenDescPluginTests
{
    private const string NfoCommand = "skycd.gendesc.nfo";
    private const string DescriptionCommand = "skycd.gendesc.description";

    static GenDescPluginTests() => GenDescTestLocalization.EnsureRegistered();

    private static MenuCommandContext Context(RecordingHostCommandApi host, string? entryId = "entry-1") =>
        new()
        {
            HostApi = host,
            Properties = entryId is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string> { ["EntryId"] = entryId }
        };

    private static GenDescPlugin Plugin(IGenDescHostApi host, string templateDirectory)
    {
        var plugin = new GenDescPlugin { TemplateDirectory = templateDirectory };
        plugin.InitializeGenDescHost(host);
        return plugin;
    }

    [Fact]
    public void ContributionsRegisterBothCommandsInTheFileMenu()
    {
        var contributions = new GenDescPlugin().GetMenuContributions();

        Assert.Equal(2, contributions.Count);
        var nfo = contributions.Single(contribution => contribution.CommandId == NfoCommand);
        var description = contributions.Single(contribution => contribution.CommandId == DescriptionCommand);
        Assert.Equal("File", nfo.Location);
        Assert.Equal("File", description.Location);
        Assert.True(nfo.RequiresLocalFile);
        Assert.True(description.RequiresLocalFile);
        Assert.Equal(10, nfo.Order);
        Assert.Equal(20, description.Order);
        Assert.Equal(LocalizationService.Current.Get(GenDescKeys.GenerateNfo), nfo.Title);
        Assert.Equal(LocalizationService.Current.Get(GenDescKeys.GenerateDescription), description.Title);
    }

    [Fact]
    public void OptionsPageAndTranslationsAreExposed()
    {
        var plugin = new GenDescPlugin();

        Assert.Equal("gendesc-templates", plugin.OptionsPage.PageId);
        Assert.Equal(LocalizationService.Current.Get(GenDescKeys.OptionsTitle), plugin.OptionsPage.Title);
        Assert.Equal(50, plugin.OptionsPage.Order);
        var translations = plugin.GetTranslations();
        Assert.Contains("en", translations.Keys);
        Assert.Contains("lt", translations.Keys);
    }

    [Fact]
    public async Task UnknownCommandsAreIgnored()
    {
        using var workspace = new TestWorkspace();
        var plugin = Plugin(new FakeGenDescHostApi(GenDescTestData.Movie()), workspace.Root);
        var host = new RecordingHostCommandApi();

        await plugin.ExecuteMenuCommandAsync("skycd.gendesc.other", Context(host));

        Assert.Empty(host.Messages);
    }

    [Fact]
    public async Task MissingSelectionIsReported()
    {
        using var workspace = new TestWorkspace();
        var plugin = Plugin(new FakeGenDescHostApi(GenDescTestData.Movie()), workspace.Root);
        var host = new RecordingHostCommandApi();

        await plugin.ExecuteMenuCommandAsync(NfoCommand, Context(host, entryId: null));

        Assert.Equal(LocalizationService.Current.Get(GenDescKeys.NoSelection), Assert.Single(host.Messages));
    }

    [Fact]
    public async Task MissingHostApiIsReported()
    {
        using var workspace = new TestWorkspace();
        var plugin = new GenDescPlugin { TemplateDirectory = workspace.Root };
        var host = new RecordingHostCommandApi();

        await plugin.ExecuteMenuCommandAsync(DescriptionCommand, Context(host));

        Assert.Equal(LocalizationService.Current.Get(GenDescKeys.HostUnavailable), Assert.Single(host.Messages));
    }

    [Fact]
    public async Task UnavailableEntriesAreReported()
    {
        using var workspace = new TestWorkspace();
        var fakeHost = new FakeGenDescHostApi((GenDescEntryContext?)null);
        var plugin = Plugin(fakeHost, workspace.Root);
        var host = new RecordingHostCommandApi();

        await plugin.ExecuteMenuCommandAsync(NfoCommand, Context(host, "gone"));

        Assert.Equal("gone", fakeHost.LastEntryId);
        Assert.Equal(LocalizationService.Current.Get(GenDescKeys.FileUnavailable), Assert.Single(host.Messages));
    }

    [Fact]
    public async Task NfoIsWrittenToTheChosenPath()
    {
        using var workspace = new TestWorkspace();
        var plugin = Plugin(new FakeGenDescHostApi(GenDescTestData.Movie()), workspace.GetPath("templates"));
        var dialogs = new RecordingFileDialogApi { SaveResult = workspace.GetPath("out.nfo") };
        plugin.InitializeFileDialogApi(dialogs);
        var host = new RecordingHostCommandApi();

        await plugin.ExecuteMenuCommandAsync(NfoCommand, Context(host));

        var request = Assert.Single(dialogs.SaveRequests);
        Assert.Equal("Example.Movie.2024.1080p.nfo", request.SuggestedFileName);
        Assert.Equal(".nfo", request.DefaultExtension);
        Assert.Single(request.Filters!);
        var written = await File.ReadAllTextAsync(workspace.GetPath("out.nfo"));
        Assert.Contains("Example Movie", written);
        Assert.Contains("File: Example.Movie.2024.1080p.mkv", written);
        Assert.Contains(workspace.GetPath("out.nfo"), Assert.Single(host.Messages));
    }

    [Fact]
    public async Task CancellingTheSaveDialogWritesNothing()
    {
        using var workspace = new TestWorkspace();
        var plugin = Plugin(new FakeGenDescHostApi(GenDescTestData.Movie()), workspace.GetPath("templates"));
        plugin.InitializeFileDialogApi(new RecordingFileDialogApi { SaveResult = null });
        var host = new RecordingHostCommandApi();

        await plugin.ExecuteMenuCommandAsync(NfoCommand, Context(host));

        Assert.Empty(Directory.GetFiles(workspace.GetPath("templates"), "*.nfo"));
        Assert.Empty(host.Messages);
    }

    [Fact]
    public async Task MissingFileDialogApiIsReported()
    {
        using var workspace = new TestWorkspace();
        var plugin = Plugin(new FakeGenDescHostApi(GenDescTestData.Movie()), workspace.GetPath("templates"));
        var host = new RecordingHostCommandApi();

        await plugin.ExecuteMenuCommandAsync(NfoCommand, Context(host));

        Assert.Equal(LocalizationService.Current.Get(GenDescKeys.HostUnavailable), Assert.Single(host.Messages));
    }

    [Fact]
    public async Task DescriptionOpensAHostWindow()
    {
        using var workspace = new TestWorkspace();
        var plugin = Plugin(new FakeGenDescHostApi(GenDescTestData.Movie()), workspace.GetPath("templates"));
        var dialogs = new RecordingDialogApi();
        plugin.InitializeDialogApi(dialogs);
        var host = new RecordingHostCommandApi();

        await plugin.ExecuteMenuCommandAsync(DescriptionCommand, Context(host));

        var request = Assert.Single(dialogs.Requests);
        Assert.Equal(640, request.Width);
        Assert.Equal(520, request.Height);
        Assert.Contains("Example.Movie.2024.1080p.mkv", request.Title);
        Assert.Contains(LocalizationService.Current.Get(GenDescKeys.DescriptionTitle), request.Title);
        Assert.Empty(host.Messages);
    }

    [Fact]
    public async Task BrokenTemplatesReportTheSyntaxFailure()
    {
        using var workspace = new TestWorkspace();
        var directory = workspace.GetPath("templates");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "default-description-movie.tpl"),
            "## id=default-description-movie\n## kind=description\n## category=movie\n## lang=en\n## mime=video/*\n## ext=.mkv\n{{ if Year }}broken");
        await File.WriteAllTextAsync(Path.Combine(directory, "default-description-movie-lt.tpl"),
            "## id=default-description-movie-lt\n## kind=description\n## category=movie\n## lang=lt\n## mime=video/*\n## ext=.mkv\n{{ if Year }}broken");
        var plugin = Plugin(new FakeGenDescHostApi(GenDescTestData.Movie()), directory);
        plugin.InitializeDialogApi(new RecordingDialogApi());
        var host = new RecordingHostCommandApi();

        await plugin.ExecuteMenuCommandAsync(DescriptionCommand, Context(host));

        Assert.Contains("was not found", Assert.Single(host.Messages));
    }

    [Fact]
    public async Task DescriptionViewModelCopiesTheText()
    {
        var clipboard = new RecordingClipboardApi();
        var viewModel = new DescriptionDialogViewModel("Title", "[b]description[/b]", clipboard);

        await viewModel.CopyCommand.ExecuteAsync(null);

        Assert.Equal("[b]description[/b]", Assert.Single(clipboard.Texts));
        Assert.Equal(LocalizationService.Current.Get(GenDescKeys.Copied), viewModel.StatusText);
    }

    [Fact]
    public void TemplateDirectoryDefaultsToTheSharedLocation()
    {
        Assert.EndsWith(Path.Combine("SkyCD", "gendesc", "templates"), GenDescTemplatePaths.DefaultDirectory);
    }
}
