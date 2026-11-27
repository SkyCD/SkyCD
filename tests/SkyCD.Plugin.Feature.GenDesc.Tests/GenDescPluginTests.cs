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
    public void PageCapabilitiesExposeSplitPagesInOneGroup()
    {
        var group = LocalizationService.Current.Get(GenDescKeys.OptionsGroup);
        var info = new GenDescInfoFilesOptionsPage();
        var descriptions = new GenDescPostDescriptionsOptionsPage();

        Assert.Equal("gendesc-info-files", info.OptionsPage.PageId);
        Assert.Equal(LocalizationService.Current.Get(GenDescKeys.InfoFilesTitle), info.OptionsPage.Title);
        Assert.Equal(50, info.OptionsPage.Order);
        Assert.Equal(group, info.OptionsPage.Group);

        Assert.Equal("gendesc-post-descriptions", descriptions.OptionsPage.PageId);
        Assert.Equal(LocalizationService.Current.Get(GenDescKeys.PostDescriptionsTitle),
            descriptions.OptionsPage.Title);
        Assert.Equal(51, descriptions.OptionsPage.Order);
        Assert.Equal(group, descriptions.OptionsPage.Group);
    }

    [Fact]
    public void PageCapabilitiesEditOnlyTheirOwnTemplateKind()
    {
        using var workspace = new TestWorkspace();
        var info = new GenDescInfoFilesOptionsPage { TemplateDirectory = workspace.Root };
        var descriptions = new GenDescPostDescriptionsOptionsPage { TemplateDirectory = workspace.Root };

        Assert.All(info.CreateTemplatesViewModel().Templates,
            template => Assert.Equal(TemplateInfo.NfoKind, template.Kind));
        Assert.All(descriptions.CreateTemplatesViewModel().Templates,
            template => Assert.Equal(TemplateInfo.DescriptionKind, template.Kind));
    }

    [Fact]
    public void TranslationsCoverBothLanguages()
    {
        var translations = new GenDescPlugin().GetTranslations();

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
    public async Task NfoCommandOpensTheSaveDialog()
    {
        using var workspace = new TestWorkspace();
        var plugin = Plugin(new FakeGenDescHostApi(GenDescTestData.Movie()), workspace.GetPath("templates"));
        var dialogs = new RecordingDialogApi();
        plugin.InitializeDialogApi(dialogs);
        var host = new RecordingHostCommandApi();

        await plugin.ExecuteMenuCommandAsync(NfoCommand, Context(host));

        var request = Assert.Single(dialogs.Requests);
        Assert.Equal(LocalizationService.Current.Get(GenDescKeys.SaveNfoTitle) + " - Example.Movie.2024.1080p.mkv",
            request.Title);
        Assert.Equal(520, request.Width);
        Assert.Equal(240, request.Height);
        Assert.Empty(host.Messages);
    }

    [Fact]
    public async Task NfoSaveDialogWritesTheChosenFile()
    {
        using var workspace = new TestWorkspace();
        var picker = new RecordingFilePicker { SaveResult = workspace.GetPath("out.nfo") };
        var savedPaths = new List<string>();
        var viewModel = new NfoSaveDialogViewModel("title", "Example.Movie.2024.1080p.nfo",
            "Example Movie\nFile: Example.Movie.2024.1080p.mkv", path =>
            {
                savedPaths.Add(path);
                return Task.CompletedTask;
            });

        await viewModel.SaveAsync(picker);

        var request = Assert.Single(picker.SaveRequests);
        Assert.Equal("Example.Movie.2024.1080p.nfo", request.SuggestedFileName);
        Assert.Equal("*.nfo", request.FileType.Pattern);
        var written = await File.ReadAllTextAsync(workspace.GetPath("out.nfo"));
        Assert.Contains("Example Movie", written);
        Assert.Equal(workspace.GetPath("out.nfo"), Assert.Single(savedPaths));
        Assert.Equal(LocalizationService.Current.Format(GenDescKeys.NfoSaved, workspace.GetPath("out.nfo")),
            viewModel.StatusText);
        Assert.Equal(string.Empty, viewModel.ErrorText);
    }

    [Fact]
    public async Task CancellingThePickerWritesNothing()
    {
        using var workspace = new TestWorkspace();
        var saved = 0;
        var viewModel = new NfoSaveDialogViewModel("title", "out.nfo", "content", _ =>
        {
            saved++;
            return Task.CompletedTask;
        });

        await viewModel.SaveAsync(new RecordingFilePicker { SaveResult = null });

        Assert.Equal(0, saved);
        Assert.Equal(string.Empty, viewModel.StatusText);
        Assert.Empty(Directory.GetFiles(workspace.Root, "*.nfo"));
    }

    [Fact]
    public async Task NfoSaveDialogWithoutPickerReportsIt()
    {
        var viewModel = new NfoSaveDialogViewModel("title", "out.nfo", "content");

        await viewModel.SaveAsync(null);

        Assert.Equal(LocalizationService.Current.Get(GenDescKeys.NoPicker), viewModel.ErrorText);
    }

    [Fact]
    public async Task NfoWithoutDialogApiIsReported()
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
    public void DescriptionDialogReportsCopied()
    {
        var viewModel = new DescriptionDialogViewModel("Title", "[b]description[/b]");

        viewModel.MarkCopied();

        Assert.Equal("[b]description[/b]", viewModel.Text);
        Assert.Equal(LocalizationService.Current.Get(GenDescKeys.Copied), viewModel.StatusText);
    }

    [Fact]
    public void TemplateDirectoryDefaultsToTheSharedLocation()
    {
        Assert.EndsWith(Path.Combine("SkyCD", "gendesc", "templates"), GenDescTemplatePaths.DefaultDirectory);
    }
}
