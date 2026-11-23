using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.App.Views;
using SkyCD.Documents.Collections;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Controls.Properties;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.UI.Tests;

/// <summary>
/// Issue #934: the description is one row in the Properties list carrying its source annotation and actions,
/// and no longer renders as a block under the comments editor on the General tab.
/// </summary>
public class PropertiesWindowDescriptionRowTests
{
    [AvaloniaFact]
    public void PropertiesList_ShowsTheStoredDescriptionOnceAsTheFirstRow()
    {
        var (window, viewModel, list) = ShowProperties(
            new PropertiesCollection { ["Description"] = "Imported description", ["Size"] = 1536 });
        try
        {
            var descriptionRow = Assert.Single(list.PropertiesRows.OfType<PropertiesRow>(), row => row.IsDescriptionRow);
            Assert.Same(list.PropertiesRows[0], descriptionRow);
            Assert.Equal(viewModel.DescriptionHeader, descriptionRow.Key);
            Assert.Equal("Imported description", descriptionRow.Value);

            var texts = RowTexts(list, viewModel.DescriptionHeader);
            Assert.Contains("Imported description", texts);
            Assert.Contains(LocalizationService.Current.Get(UiKeys.DescriptionCatalogSource), texts);

            // Without a provider the row offers no AI generation, and Copy stays available for the stored text.
            Assert.DoesNotContain(texts, text => text == LocalizationService.Current.Get(UiKeys.Generate));
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(),
                button => button.IsEffectivelyVisible && Equals(button.Command, viewModel.Description!.CopyCommand));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void GeneralTab_NoLongerShowsTheDescriptionBlock()
    {
        var (window, viewModel, _) = ShowProperties(
            new PropertiesCollection { ["Description"] = "Imported description" });
        try
        {
            var tabs = Assert.Single(window.GetVisualDescendants().OfType<TabControl>());
            tabs.SelectedItem = tabs.Items[0];
            Dispatcher.UIThread.RunJobs();

            Assert.True(viewModel.HasDescription);
            Assert.NotNull(window.FindControl<Grid>("RatingRow"));
            Assert.DoesNotContain(VisibleTexts(window), text => text == "Imported description");
            Assert.DoesNotContain(VisibleTexts(window), text => text == viewModel.Description!.SourceText);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(),
                button => button.IsEffectivelyVisible && Equals(button.Command, viewModel.Description!.CopyCommand));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DescriptionRow_FollowsTheProviderAttachedAfterTheDialogOpened()
    {
        var (window, viewModel, list) = ShowProperties(new PropertiesCollection { ["Size"] = 1536 });
        try
        {
            // Opening the dialog without a provider shows no description row or actions.
            Assert.DoesNotContain(list.PropertiesRows.OfType<PropertiesRow>(), row => row.IsDescriptionRow);
            Assert.Null(viewModel.Description);

            viewModel.Description = new DescriptionViewModel(
                "Stored text from a later read", service: null, "item");
            Dispatcher.UIThread.RunJobs();

            // The control rebuilds from the unchanged dictionary; the row appears once, first, with its value.
            var descriptionRow = Assert.Single(list.PropertiesRows.OfType<PropertiesRow>(), row => row.IsDescriptionRow);
            Assert.Same(list.PropertiesRows[0], descriptionRow);
            Assert.Equal("Stored text from a later read", descriptionRow.Value);
            Assert.Contains("Stored text from a later read",
                RowTexts(list, viewModel.DescriptionHeader));
        }
        finally
        {
            window.Close();
        }
    }

    private static string[] RowTexts(PropertiesList list, string rowHeader)
    {
        var header = Assert.Single(list.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == rowHeader);
        var row = header.GetVisualAncestors().OfType<ListBoxItem>().First();
        return row.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text))
            .Select(text => text.Text!)
            .ToArray();
    }

    private static string?[] VisibleTexts(Visual visual) =>
        visual.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible)
            .Select(text => text.Text)
            .ToArray();

    private static (PropertiesWindow Window, PropertiesDialogViewModel ViewModel, PropertiesList List) ShowProperties(
        PropertiesCollection source)
    {
        var viewModel = new PropertiesDialogViewModel("item", "item", "folder", string.Empty, source);
        var window = new PropertiesWindow { DataContext = viewModel, Width = 411, Height = 560 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var tabs = Assert.Single(window.GetVisualDescendants().OfType<TabControl>());
        tabs.SelectedItem = tabs.Items.OfType<TabItem>()
            .Single(tab => (string?)tab.Header == LocalizationService.Current.Get(UiKeys.Properties));
        Dispatcher.UIThread.RunJobs();

        var list = window.FindControl<PropertiesList>("InfoPropertiesList")
            ?? Assert.Single(window.GetVisualDescendants().OfType<PropertiesList>());
        return (window, viewModel, list);
    }
}
