using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkyCD.Plugin.Feature.GenDesc.Localization;
using SkyCD.Plugin.Feature.GenDesc.Models;
using SkyCD.Plugin.Feature.GenDesc.Parsing;
using SkyCD.Plugin.Feature.GenDesc.ViewModels;
using SkyCD.TestSupport;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.Plugin.Feature.GenDesc.Tests;

public class GenDescTemplatesViewModelTests
{
    static GenDescTemplatesViewModelTests() => GenDescTestLocalization.EnsureRegistered();

    private static GenDescTemplatesViewModel Create(string directory,
        string templateKind = TemplateInfo.DescriptionKind, RecordingFileDialogApi? dialogs = null) =>
        new(templateKind, dialogs, directory);

    [Fact]
    public void PageListsOnlyItsKindsTemplates()
    {
        using var workspace = new TestWorkspace();

        var descriptionPage = Create(workspace.Root);
        Assert.NotEmpty(descriptionPage.Templates);
        Assert.All(descriptionPage.Templates,
            template => Assert.Equal(TemplateInfo.DescriptionKind, template.Kind));
        Assert.NotNull(descriptionPage.SelectedTemplate);
        Assert.Equal(descriptionPage.SelectedTemplate!.Body, descriptionPage.EditorBody);

        var infoPage = Create(workspace.Root, TemplateInfo.NfoKind);
        Assert.NotEmpty(infoPage.Templates);
        Assert.All(infoPage.Templates, template => Assert.Equal(TemplateInfo.NfoKind, template.Kind));
        Assert.Contains(infoPage.Templates, template => template.Category == "movie");
    }

    [Fact]
    public void NewTemplatesAreCreatedWithThePageKind()
    {
        using var workspace = new TestWorkspace();
        var viewModel = Create(workspace.Root, TemplateInfo.NfoKind);
        viewModel.NewCommand.Execute(null);
        viewModel.EditorBody = "{{ Title }}";

        viewModel.SaveCommand.Execute(null);

        Assert.Equal(string.Empty, viewModel.ErrorText);
        Assert.Equal(TemplateInfo.NfoKind, TemplateStore.Load(workspace.Root).Find("custom-template")!.Kind);
        Assert.All(viewModel.Templates, template => Assert.Equal(TemplateInfo.NfoKind, template.Kind));
    }

    [Fact]
    public void SaveWritesTheEditedTemplate()
    {
        using var workspace = new TestWorkspace();
        var viewModel = Create(workspace.Root);
        viewModel.NewCommand.Execute(null);
        viewModel.EditorBody = "{{ Title }}";

        viewModel.SaveCommand.Execute(null);

        Assert.Equal(LocalizationService.Current.Get(GenDescKeys.Saved), viewModel.StatusText);
        Assert.Equal(string.Empty, viewModel.ErrorText);
        var path = Path.Combine(workspace.Root, "custom-template.tpl");
        Assert.True(File.Exists(path));
        Assert.Equal("custom-template", viewModel.SelectedTemplate!.Id);
        Assert.Equal("{{ Title }}", TemplateStore.Load(workspace.Root).Find("custom-template")!.Body);
    }

    [Fact]
    public void InvalidIdentifiersAreRejected()
    {
        using var workspace = new TestWorkspace();
        var viewModel = Create(workspace.Root);
        viewModel.NewCommand.Execute(null);
        viewModel.EditorId = "Not Valid!";

        viewModel.SaveCommand.Execute(null);

        Assert.Equal(LocalizationService.Current.Get(GenDescKeys.InvalidId), viewModel.ErrorText);
        Assert.DoesNotContain(viewModel.Templates, template => template.Id == "not valid!");
    }

    [Fact]
    public void InvalidMimeTypesAreRejected()
    {
        using var workspace = new TestWorkspace();
        var viewModel = Create(workspace.Root);
        viewModel.NewCommand.Execute(null);
        viewModel.EditorMime = "not-a-mime";

        viewModel.SaveCommand.Execute(null);

        Assert.Contains(LocalizationService.Current.Get(GenDescKeys.InvalidMime), viewModel.ErrorText);
    }

    [Fact]
    public void InvalidExtensionsAreRejected()
    {
        using var workspace = new TestWorkspace();
        var viewModel = Create(workspace.Root);
        viewModel.NewCommand.Execute(null);
        viewModel.EditorExtensions = "a/b";

        viewModel.SaveCommand.Execute(null);

        Assert.Contains(LocalizationService.Current.Get(GenDescKeys.InvalidExtension), viewModel.ErrorText);
    }

    [Fact]
    public void BrokenBodiesAreRejected()
    {
        using var workspace = new TestWorkspace();
        var viewModel = Create(workspace.Root);
        viewModel.NewCommand.Execute(null);
        viewModel.EditorBody = "{{ if Year }}unclosed";

        viewModel.SaveCommand.Execute(null);

        Assert.Contains("was not found", viewModel.ErrorText);
    }

    [Fact]
    public void PreviewRendersWithSampleFacts()
    {
        using var workspace = new TestWorkspace();
        var viewModel = Create(workspace.Root);

        viewModel.EditorBody = "{{ Name }}";

        Assert.Equal("Example.Movie.2024.1080p.BluRay.x264-GROUP", viewModel.PreviewText);
        Assert.Equal(string.Empty, viewModel.PreviewError);
    }

    [Fact]
    public void PreviewReportsSyntaxErrors()
    {
        using var workspace = new TestWorkspace();
        var viewModel = Create(workspace.Root);

        viewModel.EditorBody = "{{ Name | nope }}";

        Assert.Equal(string.Empty, viewModel.PreviewText);
        Assert.Contains("was not found", viewModel.PreviewError);
    }

    [Fact]
    public void InsertingATokenAppendsItAsABlock()
    {
        using var workspace = new TestWorkspace();
        var viewModel = Create(workspace.Root);
        viewModel.EditorBody = string.Empty;
        viewModel.SelectedToken = "Year";

        viewModel.InsertTokenCommand.Execute(null);

        Assert.Equal("{{ Year }}\n", viewModel.EditorBody);
    }

    [Fact]
    public void DeleteRemovesASavedTemplate()
    {
        using var workspace = new TestWorkspace();
        var viewModel = Create(workspace.Root);
        viewModel.NewCommand.Execute(null);
        viewModel.EditorBody = "{{ Title }}";
        viewModel.SaveCommand.Execute(null);
        var saved = viewModel.SelectedTemplate!;

        viewModel.DeleteCommand.Execute(null);

        Assert.Equal(LocalizationService.Current.Get(GenDescKeys.Deleted), viewModel.StatusText);
        Assert.DoesNotContain(viewModel.Templates, template => template.Id == saved.Id);
        Assert.False(File.Exists(Path.Combine(workspace.Root, saved.Id + ".tpl")));
    }

    [Fact]
    public void DuplicateWithoutSelectionReportsIt()
    {
        using var workspace = new TestWorkspace();
        var viewModel = Create(workspace.Root);
        viewModel.NewCommand.Execute(null);

        viewModel.DuplicateCommand.Execute(null);

        Assert.Equal(LocalizationService.Current.Get(GenDescKeys.MissingSelection), viewModel.ErrorText);
    }

    [Fact]
    public async Task ExportWritesTheSelectedTemplate()
    {
        using var workspace = new TestWorkspace();
        var dialogs = new RecordingFileDialogApi { SaveResult = workspace.GetPath("exported.tpl") };
        var viewModel = Create(workspace.Root, dialogs: dialogs);

        await viewModel.ExportCommand.ExecuteAsync(null);

        Assert.Single(dialogs.SaveRequests);
        Assert.True(File.Exists(workspace.GetPath("exported.tpl")));
        Assert.Contains("## id=", await File.ReadAllTextAsync(workspace.GetPath("exported.tpl")));
        Assert.Equal(LocalizationService.Current.Format(GenDescKeys.Exported, workspace.GetPath("exported.tpl")),
            viewModel.StatusText);
    }

    [Fact]
    public async Task ImportLoadsATemplateIntoTheEditor()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.GetPath("incoming.tpl");
        await File.WriteAllTextAsync(path,
            "## id=imported\n## kind=description\n## category=tv\n## lang=en\n{{ Name }}");
        var viewModel = Create(workspace.Root, dialogs: new RecordingFileDialogApi { OpenResult = path });

        await viewModel.ImportCommand.ExecuteAsync(null);

        Assert.Equal("imported", viewModel.EditorId);
        Assert.Equal("tv", viewModel.EditorCategory);
        Assert.Equal("{{ Name }}", viewModel.EditorBody);
        Assert.Equal(LocalizationService.Current.Format(GenDescKeys.Imported, path), viewModel.StatusText);
    }

    [Fact]
    public async Task ImportWithoutAKindUsesThePageKind()
    {
        using var workspace = new TestWorkspace();
        var sourceDirectory = workspace.GetPath("incoming");
        Directory.CreateDirectory(sourceDirectory);
        var path = Path.Combine(sourceDirectory, "incoming.tpl");
        await File.WriteAllTextAsync(path, "## id=imported-nfo\n## category=movie\n{{ Name }}");
        var viewModel = Create(workspace.Root, TemplateInfo.NfoKind,
            new RecordingFileDialogApi { OpenResult = path });

        await viewModel.ImportCommand.ExecuteAsync(null);
        viewModel.SaveCommand.Execute(null);

        Assert.Equal(string.Empty, viewModel.ErrorText);
        Assert.Equal(TemplateInfo.NfoKind, TemplateStore.Load(workspace.Root).Find("imported-nfo")!.Kind);
    }

    [Fact]
    public async Task ImportWithoutAPickerReportsIt()
    {
        using var workspace = new TestWorkspace();
        var viewModel = Create(workspace.Root);

        await viewModel.ImportCommand.ExecuteAsync(null);

        Assert.Equal(LocalizationService.Current.Get(GenDescKeys.NoPicker), viewModel.ErrorText);
    }

    [Fact]
    public void ReloadRereadsTheDirectory()
    {
        using var workspace = new TestWorkspace();
        var viewModel = Create(workspace.Root);
        var id = viewModel.SelectedTemplate!.Id;
        File.WriteAllText(Path.Combine(workspace.Root, id + ".tpl"), "hand edited");

        viewModel.ReloadCommand.Execute(null);

        var reloaded = viewModel.Templates.Single(template => template.Id == id);
        Assert.Equal("hand edited", reloaded.Body);
    }
}
