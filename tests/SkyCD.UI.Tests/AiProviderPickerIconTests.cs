using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.App.Views;
using SkyCD.Plugin.Ai.Provider.Models;
using SkyCD.Presentation.ViewModels;
using SkyCD.SDK.Capabilities.Media;
using Xunit;

namespace SkyCD.UI.Tests;

/// <summary>Issue #1207: the AI provider picker shows an icon, and a missing icon falls back to the monogram.</summary>
public sealed class AiProviderPickerIconTests
{
    [AvaloniaFact]
    public void Picker_ShowsIconAndName_AndFallsBackToTheMonogram()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetAiDescriptionAvailable(true);
        var window = new OptionsWindow
        {
            DataContext = viewModel,
            AiProviderSettingsCapability = new CatalogCapability()
        };
        window.Show();
        viewModel.SelectedTabIndex = viewModel.SettingCategories.ToList()
            .FindIndex(category => category.Id == "ai-provider");
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.ShowAiProviderSection);

        try
        {
            var picker = window.FindControl<ComboBox>("AiProviderComboBox")!;
            Assert.Contains(Texts(picker), text => text == "LM Studio");
            Assert.NotNull(Icon(picker));

            viewModel.SelectedAiProvider = viewModel.AiProviders.Single(provider => provider.Id == "custom");
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(Texts(picker), text => text == "Custom local OpenAI-compatible");
            Assert.NotNull(Icon(picker));

            picker.IsDropDownOpen = true;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var rows = picker.GetRealizedContainers().OfType<ComboBoxItem>().ToList();
            Assert.Equal(picker.ItemCount, rows.Count);
            Assert.All(rows, row =>
            {
                var choice = Assert.IsType<AiProviderChoice>(row.DataContext);
                Assert.Contains(Texts(row), text => text == choice.Name);
                Assert.NotNull(Icon(row));
            });

            viewModel.AiProviders.Clear();
            viewModel.AiProviders.Add(new AiProviderChoice(
                "missing", "Missing", "http", "127.0.0.1", 9, null, false,
                IconKey: "SkyCD.Plugin.Ai.Provider.Assets.Providers.missing.png", Monogram: "ZZ"));
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

    private static IEnumerable<string?> Texts(Control root) =>
        root.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text);

    private static Bitmap? Icon(Control root) =>
        root.GetVisualDescendants().OfType<Image>().Select(image => image.Source).OfType<Bitmap>().FirstOrDefault();

    private sealed class CatalogCapability : IAiProviderSettingsCapability
    {
        public IReadOnlyList<AiProviderChoice> Providers => AiProviderCatalog.Providers;

        public AiProviderSettings GetSettings() => new() { ProviderId = "lmstudio" };

        public void SaveSettings(AiProviderSettings settings)
        {
        }

        public string BuildEndpointUrl(AiProviderSettings settings) =>
            $"{settings.Protocol}://{settings.Address}:{settings.Port}/v1";

        public Task<AiProviderConnectionResult> CheckConnectionAsync(AiProviderSettings settings,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AiProviderConnectionResult(true, "Connected", []));

        public Task<IReadOnlyList<string>> ListModelsAsync(AiProviderSettings settings,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> StartServerAsync(AiProviderSettings settings, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }
}
