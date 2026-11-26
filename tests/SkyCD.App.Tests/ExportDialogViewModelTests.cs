using System.Collections.Generic;
using SkyCD.Presentation.Catalog.Export;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

public class ExportDialogViewModelTests
{
    private static readonly ExportFormatOption Html = new("skycd-html", "HTML catalog", "html", [".html"]);
    private static readonly ExportFormatOption Markdown = new("skycd-md", "Markdown catalog", "md", [".md"]);

    [Fact]
    public void TheFirstFormatIsSelectedByDefault()
    {
        var viewModel = new ExportDialogViewModel([Html, Markdown]);

        Assert.Equal(Html, viewModel.SelectedExportFormat);
        Assert.True(viewModel.ExportCommand.CanExecute(null));
        Assert.Null(viewModel.AcceptedFormat);
    }

    [Fact]
    public void TheDefaultFormatIdWinsWhenItMatches()
    {
        var viewModel = new ExportDialogViewModel([Html, Markdown], "skycd-md");

        Assert.Equal(Markdown, viewModel.SelectedExportFormat);
    }

    [Fact]
    public void AnUnknownDefaultFormatIdFallsBackToTheFirstFormat()
    {
        var viewModel = new ExportDialogViewModel([Html, Markdown], "skycd-json");

        Assert.Equal(Html, viewModel.SelectedExportFormat);
    }

    [Fact]
    public void ExportingAcceptsTheSelectedFormatAndRequestsClose()
    {
        var viewModel = new ExportDialogViewModel([Html, Markdown], "skycd-md");
        var closed = false;
        viewModel.CloseRequested += () => closed = true;

        viewModel.ExportCommand.Execute(null);

        Assert.Equal(Markdown, viewModel.AcceptedFormat);
        Assert.True(closed);
    }

    [Fact]
    public void WithoutFormats_ExportIsDisabled()
    {
        var viewModel = new ExportDialogViewModel([]);

        Assert.Null(viewModel.SelectedExportFormat);
        Assert.False(viewModel.ExportCommand.CanExecute(null));
        Assert.Null(viewModel.AcceptedFormat);
    }

    [Fact]
    public void ExportStrings_AreLocalized()
    {
        var keys = new[]
        {
            TextKeys.ExportFileMenu, TextKeys.ExportCatalog, TextKeys.ExportedCatalogTo,
            TextKeys.FailedToExportCatalog, TextKeys.FailedToExportCatalogAccessDenied,
            TextKeys.FailedToExportCatalogNoExportFormatCheckPluginSettings,
            UiKeys.ExportDialogTitle, UiKeys.ExportFormat, UiKeys.ExportAction, UiKeys.ExportNoFormat
        };
        Assert.All(keys, key => Assert.NotEqual(key, LocalizationService.Current.Get(key)));
    }
}
