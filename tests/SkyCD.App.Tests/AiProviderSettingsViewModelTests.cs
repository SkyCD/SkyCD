using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

public class AiProviderSettingsViewModelTests
{
    private static readonly string AiGroup = LocalizationService.Current.Get(UiKeys.AiGroup);

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
        var descriptionIndex = categories.FindIndex(category => category.Title == "Description");
        var providerIndex = categories.FindIndex(category => category.Title == "Provider");
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
        vm.SelectedTabIndex = vm.SettingCategories.ToList().FindIndex(category => category.Id == "ai-provider");
        Assert.True(vm.IsAiProviderCategorySelected);

        vm.SetAiDescriptionAvailable(false);

        Assert.DoesNotContain(vm.SettingCategories, category => category.Id == "ai-provider");
        Assert.Equal(6, vm.SelectedTabIndex);
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

        Assert.Equal(["Provider"], vm.FilteredSettingCategories.Select(category => category.Title));
        Assert.Equal("Provider", vm.SelectedSettingCategory?.Title);
        Assert.True(vm.IsAiProviderCategorySelected);
        Assert.True(vm.ShowAiProviderSection);
    }

    [Fact]
    public void AiCategories_RenderUnderSingleGroupHeaderWithReadableLabels()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetAiDescriptionAvailable(true);

        var header = Assert.Single(vm.SidebarEntries, entry => entry.IsGroupHeader && entry.Title == AiGroup);
        Assert.Equal(AiGroup, header.Title);
        var headerIndex = vm.SidebarEntries.IndexOf(header);
        var children = vm.SidebarEntries.Skip(headerIndex + 1)
            .TakeWhile(entry => !entry.IsGroupHeader)
            .ToList();
        Assert.Equal(["Description", "Provider", "MCP Server", "ACP agents", "MCP Tools"],
            children.Select(child => child.Title));
        Assert.Equal(["ai-description", "ai-provider", "mcp", "acp", "mcp-tools"],
            children.Select(child => child.Category?.Id));
        Assert.All(children, child => Assert.Equal(1, child.Depth));
    }

    [Fact]
    public void SelectingAiGroupHeader_SelectsDescriptionAndShowsFullPath()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetAiDescriptionAvailable(true);
        var header = Assert.Single(vm.SidebarEntries, entry => entry.IsGroupHeader && entry.Title == AiGroup);

        vm.SelectedSidebarEntry = header;

        Assert.Equal("ai-description", vm.SelectedSettingCategory?.Id);
        Assert.False(vm.SelectedSidebarEntry!.IsGroupHeader);
        Assert.Equal($"{AiGroup}  >  Description", vm.CurrentCategoryPath);
        Assert.True(vm.ShowAiDescriptionSection);
    }

    [Fact]
    public void Search_FiltersAiChildrenAndKeepsGroupForAbbreviation()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetAiDescriptionAvailable(true);

        vm.SettingsSearchText = "provider";

        var header = Assert.Single(vm.SidebarEntries, entry => entry.IsGroupHeader && entry.Title == AiGroup);
        Assert.Equal(AiGroup, header.Title);
        var child = Assert.Single(vm.SidebarEntries, entry => !entry.IsGroupHeader);
        Assert.Equal("Provider", child.Title);
        Assert.Equal("ai-provider", child.Category?.Id);

        vm.SettingsSearchText = "AI";

        header = Assert.Single(vm.SidebarEntries, entry => entry.IsGroupHeader && entry.Title == AiGroup);
        Assert.Equal(AiGroup, header.Title);
        var filteredIds = vm.FilteredSettingCategories.Select(category => category.Id).ToList();
        Assert.Contains("ai-description", filteredIds);
        Assert.Contains("ai-provider", filteredIds);
        Assert.Contains("mcp", filteredIds);
        Assert.Contains("acp", filteredIds);
        Assert.Contains("mcp-tools", filteredIds);
    }

    [Fact]
    public void UnavailableAi_RemovesAiPagesAndKeepsMcpPagesUnderGroup()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetAiDescriptionAvailable(true);
        vm.SelectedSidebarEntry = vm.SidebarEntries.Single(entry => entry.Category?.Id == "ai-provider");

        vm.SetAiDescriptionAvailable(false);

        var header = Assert.Single(vm.SidebarEntries, entry => entry.IsGroupHeader && entry.Title == AiGroup);
        Assert.Equal(AiGroup, header.Title);
        var headerIndex = vm.SidebarEntries.IndexOf(header);
        var children = vm.SidebarEntries.Skip(headerIndex + 1)
            .TakeWhile(entry => !entry.IsGroupHeader)
            .ToList();
        Assert.Equal(["mcp", "acp", "mcp-tools"], children.Select(child => child.Category?.Id));
        Assert.DoesNotContain(vm.SettingCategories, category => category.Id.StartsWith("ai-", StringComparison.Ordinal));
        Assert.Equal("toolbar", vm.SelectedSettingCategory?.Id);
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
