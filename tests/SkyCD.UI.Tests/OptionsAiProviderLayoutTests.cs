using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.App.Views;
using SkyCD.Plugin.Host.Media;
using SkyCD.Presentation.ViewModels;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.TestSupport;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.UI.Tests;

/// <summary>
/// Issue #1206: the AI provider page follows the shared Options form pattern — one label column,
/// one section header, field-aligned actions — and the token gate keeps it that way.
/// </summary>
public sealed class OptionsAiProviderLayoutTests : IDisposable
{
    private readonly AppDataSandbox sandbox = new();

    public void Dispose() => sandbox.Dispose();

    [AvaloniaFact]
    public void AiSettingsRows_ShareOneLabelWidth()
    {
        var window = ShowAiWindow(out _, "ai-provider");
        try
        {
            var widths = SettingsRowLabelWidths(window, "ai-provider");
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
    public void AiDescriptionRows_UseTheSharedLabelWidth()
    {
        var window = ShowAiWindow(out _, "ai-description");
        try
        {
            var widths = SettingsRowLabelWidths(window, "ai-description");
            Assert.True(widths.Length >= 2, $"Expected the description settings rows, found {widths.Length}.");
            Assert.All(widths, width => Assert.InRange(width, 187.5, 188.5));
            Assert.Single(widths.Distinct().ToArray());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void PortLabel_CarriesNoMarginHack()
    {
        var window = ShowAiWindow(out _, "ai-provider");
        try
        {
            var card = ProviderCard(window);
            var labels = card.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible)
                .ToArray();

            Assert.NotEmpty(labels);
            Assert.All(labels, label =>
                Assert.Equal(new Thickness(0), label.Margin));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Actions_AlignWithTheFieldColumn()
    {
        var window = ShowAiWindow(out _, "ai-provider");
        try
        {
            var card = ProviderCard(window);
            var fields = card.GetVisualDescendants().OfType<ComboBox>()
                .Where(box => box.IsEffectivelyVisible)
                .ToArray();
            var checkBox = card.GetVisualDescendants().OfType<CheckBox>()
                .Single(box => box.IsEffectivelyVisible);
            var actions = card.GetVisualDescendants().OfType<StackPanel>()
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
    public void ProviderSection_HasNoInPageTitleRepeatingTheBreadcrumb()
    {
        // Issue #1213: the in-page "AI provider" title duplicated the breadcrumb header.
        var window = ShowAiWindow(out _, "ai-provider");
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

    [Fact]
    public void ProviderMarkup_HasNoInCardHeadingReference()
    {
        var markup = File.ReadAllText(FindOptionsWindow());

        Assert.DoesNotContain("AiProviderSettings}", markup, StringComparison.Ordinal);
    }

    private static OptionsWindow ShowAiWindow(out OptionsDialogViewModel viewModel, string categoryId)
    {
        viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.LoadAiProviderSettings(new FakeCapability());
        viewModel.SetAiDescriptionAvailable(true);
        viewModel.SetAiDescriptionImplementations(
            [new("acp", "ACP"), new("provider", "AI provider")], "acp");
        viewModel.SelectedTabIndex = viewModel.SettingCategories.ToList()
            .FindIndex(category => category.Id == categoryId);

        // The window reloads provider settings through AiProviderSettingsCapability when it
        // opens, so the test supplies the same fake capability instead of letting the
        // production lookup clear it to null.
        var window = new OptionsWindow
        {
            DataContext = viewModel,
            AiProviderSettingsCapability = new FakeCapability()
        };
        window.Width = 900;
        window.Height = 700;
        window.Show();
        viewModel.SelectedTabIndex = viewModel.SettingCategories.ToList()
            .FindIndex(category => category.Id == categoryId);
        Dispatcher.UIThread.RunJobs();

        Assert.True(viewModel.ShowAiProviderSection || categoryId == "ai-description" && viewModel.ShowAiDescriptionSection,
            $"The {categoryId} section never became visible.");
        return window;
    }

    private static double[] SettingsRowLabelWidths(OptionsWindow window, string categoryId)
    {
        var scope = categoryId == "ai-provider"
            ? (Visual)ProviderCard(window)
            : DescriptionScope(window);

        // Control templates (combo box toggles) carry their own centered labels; only the
        // page-level settings rows count. Avalonia reports the trailing column gap inside
        // ActualWidth, so the shared 180 px label reads back as 180 + 8.
        return scope.GetVisualDescendants().OfType<Grid>()
            .Where(grid => grid.IsEffectivelyVisible
                && !grid.GetVisualAncestors().OfType<ComboBox>().Any()
                && grid.ColumnDefinitions.Count >= 2
                && grid.GetVisualChildren().FirstOrDefault() is TextBlock label
                && label.VerticalAlignment == VerticalAlignment.Center)
            .Select(grid => grid.ColumnDefinitions[0].ActualWidth)
            .ToArray();
    }

    private static Visual ProviderCard(OptionsWindow window) =>
        window.GetVisualDescendants().OfType<ScrollViewer>()
            .Single(scroll => scroll.IsEffectivelyVisible
                && scroll.GetVisualDescendants().OfType<ComboBox>().Any());

    private static Visual DescriptionScope(OptionsWindow window) =>
        window.GetVisualDescendants().OfType<ScrollViewer>()
            .Single(scroll => scroll.Name == "AiDescriptionSettingsScrollViewer");

    private static string FindOptionsWindow()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "src", "SkyCD.App", "Views", "OptionsWindow.axaml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("OptionsWindow.axaml was not found.");
    }

    private sealed class FakeCapability : IAiProviderSettingsCapability
    {
        private static readonly AiProviderChoice Colibri = new("colibri", "Colibri", "http", "127.0.0.1",
            8000, "model-a", true, ["model-a", "model-b"]);

        private static readonly AiProviderChoice Ollama = new("ollama", "Ollama", "http", "127.0.0.1",
            11434, "llama3.2", true, ["llama3.2", "qwen3"]);

        public IReadOnlyList<AiProviderChoice> Providers { get; } = [Colibri, Ollama];

        public AiProviderSettings GetSettings() => new();

        public void SaveSettings(AiProviderSettings settings)
        {
        }

        public string BuildEndpointUrl(AiProviderSettings settings) =>
            $"{settings.Protocol}://{settings.Address}:{settings.Port}/v1";

        public Task<AiProviderConnectionResult> CheckConnectionAsync(AiProviderSettings settings,
            CancellationToken cancellationToken) => Task.FromResult(new AiProviderConnectionResult(true,
            "Connected", Array.Empty<string>()));

        public Task<IReadOnlyList<string>> ListModelsAsync(AiProviderSettings settings,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task<string?> StartServerAsync(AiProviderSettings settings, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }
}
