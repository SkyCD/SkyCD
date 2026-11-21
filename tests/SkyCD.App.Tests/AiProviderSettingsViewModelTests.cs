using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public class AiProviderSettingsViewModelTests
{
    private static readonly AiProviderChoice LmStudio = new("lmstudio", "LM Studio", "http", "127.0.0.1", 1234,
        "model-a", true, ["model-a", "model-b"]);

    private static readonly AiProviderChoice Ollama = new("ollama", "Ollama", "http", "127.0.0.1", 11434,
        "llama3.2", true, ["llama3.2", "qwen3"]);

    [Fact]
    public void LoadAiProviderSettings_AppliesSavedValuesAndEndpoint()
    {
        var capability = new FakeCapability
        {
            Saved = new AiProviderSettings
            {
                ProviderId = "ollama",
                Protocol = "http",
                Address = "127.0.0.1",
                Port = 11434,
                Model = "qwen3",
                Language = "English",
                Enabled = true
            }
        };
        var vm = new OptionsDialogViewModel(["English"]);

        vm.LoadAiProviderSettings(capability);

        Assert.Equal(2, vm.AiProviders.Count);
        Assert.Equal("ollama", vm.SelectedAiProvider?.Id);
        Assert.Equal("http", vm.AiProviderProtocol);
        Assert.Equal("127.0.0.1", vm.AiProviderAddress);
        Assert.Equal(11434, vm.AiProviderPort);
        Assert.Equal("qwen3", vm.AiProviderModel);
        Assert.Contains("qwen3", vm.AiProviderModels);
        Assert.Equal("http://127.0.0.1:11434/v1", vm.AiProviderEndpointUrl);
    }

    [Fact]
    public void SelectingAnotherProvider_TogglesSuggestedModelsAndDefaults()
    {
        var capability = new FakeCapability();
        var vm = new OptionsDialogViewModel(["English"]);
        vm.LoadAiProviderSettings(capability);

        vm.SelectedAiProvider = vm.AiProviders.Single(provider => provider.Id == "ollama");

        Assert.Equal("http", vm.AiProviderProtocol);
        Assert.Equal("127.0.0.1", vm.AiProviderAddress);
        Assert.Equal(11434, vm.AiProviderPort);
        Assert.Equal("llama3.2", vm.AiProviderModel);
        Assert.Equal(new[] { "llama3.2", "qwen3" }, vm.AiProviderModels);
        Assert.Equal("http://127.0.0.1:11434/v1", vm.AiProviderEndpointUrl);
        Assert.True(vm.AiProviderCanStartServer);
    }

    [Fact]
    public void AiProviderCategory_ShowsProviderSettingsIndependentlyOfDescriptionPage()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.LoadAiProviderSettings(new FakeCapability());
        vm.SetAiDescriptionAvailable(true);
        vm.SetAiDescriptionImplementations(
            [new("acp", "ACP"), new("provider", "AI provider")], "acp");

        var categories = vm.SettingCategories.ToList();
        var descriptionIndex = categories.FindIndex(category => category.Title == "AI description");
        var providerIndex = categories.FindIndex(category => category.Title == "AI provider");
        Assert.Equal(descriptionIndex + 1, providerIndex);

        vm.SelectedTabIndex = descriptionIndex;
        Assert.True(vm.ShowAiDescriptionSection);
        Assert.False(vm.ShowAiProviderSection);

        vm.SelectedTabIndex = providerIndex;
        Assert.True(vm.IsAiProviderCategorySelected);
        Assert.True(vm.ShowAiProviderSection);
        Assert.False(vm.ShowAiDescriptionSection);
    }

    [Fact]
    public void AiProviderCategory_IsHiddenAndSelectionFallsBackWhenDescriptionUnavailable()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.LoadAiProviderSettings(new FakeCapability());
        vm.SetAiDescriptionAvailable(true);
        vm.SelectedTabIndex = vm.SettingCategories.Count - 1;
        Assert.True(vm.IsAiProviderCategorySelected);

        vm.SetAiDescriptionAvailable(false);

        Assert.DoesNotContain(vm.SettingCategories, category => category.Title == "AI provider");
        Assert.Equal(9, vm.SelectedTabIndex);
        Assert.True(vm.IsToolbarCategorySelected);
        Assert.False(vm.ShowAiProviderSection);
    }

    [Fact]
    public void SearchingProvider_SelectsAiProviderCategory()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.LoadAiProviderSettings(new FakeCapability());
        vm.SetAiDescriptionAvailable(true);

        vm.SettingsSearchText = "provider";

        Assert.Equal(["AI provider"], vm.FilteredSettingCategories.Select(category => category.Title));
        Assert.Equal("AI provider", vm.SelectedSettingCategory?.Title);
        Assert.True(vm.IsAiProviderCategorySelected);
        Assert.True(vm.ShowAiProviderSection);
    }

    private sealed class FakeCapability : IAiProviderSettingsCapability
    {
        public IReadOnlyList<AiProviderChoice> Providers { get; } = [LmStudio, Ollama];

        public AiProviderSettings Saved { get; set; } = new();

        public AiProviderSettings GetSettings() => Saved;

        public void SaveSettings(AiProviderSettings settings) => Saved = settings;

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
