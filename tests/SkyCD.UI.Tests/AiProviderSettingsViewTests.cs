using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.App.Views;
using SkyCD.Plugin.Ai.Provider;
using SkyCD.Plugin.Ai.Provider.Configuration;
using SkyCD.Plugin.Ai.Provider.Models;
using SkyCD.Plugin.Ai.Provider.Views;
using SkyCD.Presentation.ViewModels;
using SkyCD.TestSupport;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.UI.Tests;

/// <summary>
/// Issues #1206, #1207, #1213: the AI provider configuration page moved from the host into the
/// plugin (#1340) and keeps the shared form pattern — one label column, field-aligned actions,
/// no repeated title — and a provider icon with a monogram fallback.
/// </summary>
public sealed class AiProviderSettingsViewTests : IDisposable
{
    private readonly TestWorkspace workspace = new();

    public void Dispose() => workspace.Dispose();

    private AiProviderSettingsPageViewModel CreatePageViewModel() =>
        new(new AiProviderPlugin(Path.Combine(workspace.Root, "provider.json")),
            new System.Collections.Generic.Dictionary<string, string?>());

    private static Window ShowView(Control content)
    {
        var window = new Window { Content = content, Width = 900, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void AiSettingsRows_ShareOneLabelWidth()
    {
        using var viewModel = CreatePageViewModel();
        var window = ShowView(new AiProviderSettingsView(viewModel));
        try
        {
            var widths = SettingsRowLabelWidths(window);
            Assert.True(widths.Length >= 7, $"Expected the provider settings rows, found {widths.Length}.");
            Assert.All(widths, width => Assert.InRange(width, 187.5, 188.5));
            Assert.Single(widths.Distinct().ToArray());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AiSettingsLabels_CarryNoMarginHack()
    {
        using var viewModel = CreatePageViewModel();
        var window = ShowView(new AiProviderSettingsView(viewModel));
        try
        {
            var labels = window.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible)
                .ToArray();

            Assert.NotEmpty(labels);
            Assert.All(labels, label => Assert.Equal(new Thickness(0), label.Margin));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Actions_AlignWithTheFieldColumn()
    {
        using var viewModel = CreatePageViewModel();
        var window = ShowView(new AiProviderSettingsView(viewModel));
        try
        {
            var fields = window.GetVisualDescendants().OfType<ComboBox>()
                .Where(box => box.IsEffectivelyVisible)
                .ToArray();
            var checkBox = window.GetVisualDescendants().OfType<CheckBox>()
                .Single(box => box.IsEffectivelyVisible);
            var actions = window.GetVisualDescendants().OfType<StackPanel>()
                .Where(panel => panel.Orientation == Orientation.Horizontal)
                .Single(panel => panel.GetVisualDescendants().OfType<Button>().Any());

            Assert.NotEmpty(fields);
            var fieldX = fields[0].TranslatePoint(default, window)!.Value.X;
            var checkX = checkBox.TranslatePoint(default, window)!.Value.X;
            var actionsX = actions.TranslatePoint(default, window)!.Value.X;

            Assert.InRange(checkX - fieldX, -1, 1);
            Assert.InRange(actionsX - fieldX, -1, 1);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ProviderPage_HasNoInPageTitleRepeatingTheBreadcrumb()
    {
        using var viewModel = CreatePageViewModel();
        var window = ShowView(new AiProviderSettingsView(viewModel));
        try
        {
            var header = LocalizationService.Current.Get(UiKeys.AiProviderCategory);
            var duplicates = window.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible && text.Text == header)
                .ToArray();

            Assert.Empty(duplicates);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Picker_ShowsIconAndName_AndFallsBackToTheMonogram()
    {
        using var viewModel = CreatePageViewModel();
        var view = new AiProviderSettingsView(viewModel);
        var window = ShowView(view);
        try
        {
            var picker = Assert.IsType<ComboBox>(view.FindControl<ComboBox>("AiProviderComboBox"));
            Assert.Contains(Texts(picker), text => text == "Ollama");
            Assert.NotNull(Icon(picker));
            Assert.DoesNotContain("OL", Texts(picker));

            viewModel.SelectedAiProvider = viewModel.AiProviders.Single(provider => provider.Id == "custom");
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(Texts(picker), text => text == "Custom local OpenAI-compatible");
            Assert.Null(Icon(picker));
            Assert.Contains("AI", Texts(picker));

            picker.IsDropDownOpen = true;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var rows = picker.GetRealizedContainers().OfType<ComboBoxItem>().ToList();
            Assert.Equal(picker.ItemCount, rows.Count);
            Assert.All(rows, row =>
            {
                var choice = Assert.IsType<AiProviderChoice>(row.DataContext);
                Assert.Contains(Texts(row), text => text == choice.Name);
                if (choice.IconKey is null)
                {
                    Assert.Null(Icon(row));
                    Assert.Contains(choice.Monogram, Texts(row));
                }
                else
                {
                    Assert.NotNull(Icon(row));
                    Assert.DoesNotContain(choice.Monogram, Texts(row));
                }
            });

            viewModel.AiProviders.Clear();
            viewModel.AiProviders.Add(new AiProviderChoice(
                "missing", "Missing", "http", "127.0.0.1", 9, null, false,
                IconKey: "llm-backend:missing", Monogram: "ZZ"));
            viewModel.SelectedAiProvider = viewModel.AiProviders[0];
            picker.IsDropDownOpen = false;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(Texts(picker), text => text == "Missing");
            Assert.Contains(Texts(picker), text => text == "ZZ");
            Assert.Null(Icon(picker));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AiDescriptionRows_UseTheSharedLabelWidth()
    {
        using var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetAiDescriptionAvailable(true);
        viewModel.SetAiDescriptionImplementations(
            [new("acp", "ACP"), new("provider", "AI provider")], "acp");
        viewModel.SelectedTabIndex = viewModel.SettingCategories.ToList()
            .FindIndex(category => category.Id == "ai-description");
        var window = new OptionsWindow { DataContext = viewModel, Width = 900, Height = 700 };
        window.Show();
        viewModel.SelectedTabIndex = viewModel.SettingCategories.ToList()
            .FindIndex(category => category.Id == "ai-description");
        Dispatcher.UIThread.RunJobs();

        try
        {
            Assert.True(viewModel.ShowAiDescriptionSection);
            var scope = window.GetVisualDescendants().OfType<ScrollViewer>()
                .Single(scroll => scroll.Name == "AiDescriptionSettingsScrollViewer");
            var widths = SettingsRowLabelWidths(scope);
            Assert.True(widths.Length >= 2, $"Expected the description settings rows, found {widths.Length}.");
            Assert.All(widths, width => Assert.InRange(width, 187.5, 188.5));
            Assert.Single(widths.Distinct().ToArray());
        }
        finally
        {
            window.Close();
            viewModel.Dispose();
        }
    }

    [Fact]
    public void OptionsWindow_NoLongerHostsTheProviderSection()
    {
        var markup = File.ReadAllText(FindRepoFile("src", "SkyCD.App", "Views", "OptionsWindow.axaml"));

        Assert.DoesNotContain("ShowAiProviderSection", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("AiProviderSettings}", markup, StringComparison.Ordinal);
    }

    private static IEnumerable<string?> Texts(Control root) =>
        root.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible)
            .Select(block => block.Text);

    private static Bitmap? Icon(Control root) =>
        root.GetVisualDescendants().OfType<Image>().Select(image => image.Source).OfType<Bitmap>().FirstOrDefault();

    private static double[] SettingsRowLabelWidths(Visual scope) =>
        scope.GetVisualDescendants().OfType<Grid>()
            .Where(grid => grid.IsEffectivelyVisible
                && !grid.GetVisualAncestors().OfType<ComboBox>().Any()
                && grid.ColumnDefinitions.Count >= 2
                && grid.GetVisualChildren().FirstOrDefault() is TextBlock label
                && label.VerticalAlignment == VerticalAlignment.Center)
            .Select(grid => grid.ColumnDefinitions[0].ActualWidth)
            .ToArray();

    private static string FindRepoFile(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine([current.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"File '{Path.Combine(parts)}' was not found.");
    }
}
