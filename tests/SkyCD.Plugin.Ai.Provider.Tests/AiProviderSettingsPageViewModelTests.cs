using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkyCD.Plugin.Ai.Provider.Configuration;
using SkyCD.Plugin.Ai.Provider.Models;
using SkyCD.UI.Localization;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.Plugin.Ai.Provider.Tests;

public sealed class AiProviderSettingsPageViewModelTests : IDisposable
{
    private readonly TestWorkspace workspace = new();

    public void Dispose() => workspace.Dispose();

    private string configurationPath => Path.Combine(workspace.Root, "provider.json");

    private AiProviderPlugin CreatePlugin() => new(configurationPath);

    [Fact]
    public void Configurations_ExposeTheProviderSettingsPage()
    {
        var plugin = CreatePlugin();

        var configuration = Assert.Single(plugin.Configurations);

        Assert.Equal(AiProviderPlugin.SettingsConfigurationId, configuration.ConfigurationId);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.AiProviderCategory), configuration.Title);
        Assert.Equal(-10, configuration.Order);
    }

    [Fact]
    public void LoadsSavedSettingsAndSeedsSuggestedModels()
    {
        var plugin = CreatePlugin();
        plugin.SaveSettings(new AiProviderSettings
        {
            ProviderId = "ollama",
            Protocol = "http",
            Address = "127.0.0.1",
            Port = 11434,
            Model = "qwen3",
            Language = "English",
            Enabled = true
        });

        using var page = new AiProviderSettingsPageViewModel(plugin, new Dictionary<string, string?>());

        Assert.Equal(AiProviderCatalog.Providers.Count, page.AiProviders.Count);
        Assert.Equal("ollama", page.SelectedAiProvider?.Id);
        Assert.Equal("http", page.AiProviderProtocol);
        Assert.Equal("127.0.0.1", page.AiProviderAddress);
        Assert.Equal(11434, page.AiProviderPort);
        Assert.Equal("qwen3", page.AiProviderModel);
        Assert.Contains("qwen3", page.AiProviderModels);
        Assert.Equal("http://127.0.0.1:11434/v1", page.AiProviderEndpointUrl);
        Assert.True(page.AiProviderCanStartServer);
    }

    [Fact]
    public void SelectingProvider_AppliesPresetDefaultsAndEndpoint()
    {
        var plugin = CreatePlugin();
        using var page = new AiProviderSettingsPageViewModel(plugin, new Dictionary<string, string?>());

        page.SelectedAiProvider = page.AiProviders.Single(provider => provider.Id == "ollama");

        var ollama = AiProviderCatalog.Find("ollama")!;
        Assert.Equal(11434, page.AiProviderPort);
        Assert.Equal("llama3.2", page.AiProviderModel);
        Assert.Equal(ollama.SuggestedModels, page.AiProviderModels);
        Assert.Equal("http://127.0.0.1:11434/v1", page.AiProviderEndpointUrl);
        Assert.True(page.AiProviderCanStartServer);
    }

    [Fact]
    public void SelectingProviderWithoutDefaults_ClearsModelAndDisablesServer()
    {
        var plugin = CreatePlugin();
        using var page = new AiProviderSettingsPageViewModel(plugin, new Dictionary<string, string?>());

        page.SelectedAiProvider = page.AiProviders.Single(provider => provider.Id == "custom");

        Assert.Empty(page.AiProviderModels);
        Assert.Equal(string.Empty, page.AiProviderModel);
        Assert.False(page.AiProviderCanStartServer);
    }

    [Fact]
    public void EditingProtocolAddressAndPort_UpdatesTheEndpoint()
    {
        var plugin = CreatePlugin();
        using var page = new AiProviderSettingsPageViewModel(plugin, new Dictionary<string, string?>());

        page.AiProviderAddress = "192.168.1.50";
        page.AiProviderPort = 8000;

        Assert.Equal("http://192.168.1.50:8000/v1", page.AiProviderEndpointUrl);

        page.AiProviderProtocol = "https";

        Assert.Equal("https://192.168.1.50:8000/v1", page.AiProviderEndpointUrl);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(70000, 65535)]
    public void Port_IsClampedToTheSupportedRange(int entered, int expected)
    {
        var plugin = CreatePlugin();
        using var page = new AiProviderSettingsPageViewModel(plugin, new Dictionary<string, string?>());

        page.AiProviderPort = entered;

        Assert.Equal(expected, page.AiProviderPort);
    }

    [Fact]
    public void ApiKeyHelp_FollowsProviderSelection()
    {
        var plugin = CreatePlugin();
        using var page = new AiProviderSettingsPageViewModel(plugin, new Dictionary<string, string?>());

        page.SelectedAiProvider = page.AiProviders.Single(provider => provider.Id == "llamacpp");

        Assert.Equal(AiProviderCatalog.Find("llamacpp")!.ApiKeyHelpUrl, page.ApiKeyHelpUrl);
        Assert.True(page.ShowApiKeyHelp);
        Assert.True(page.OpenAiProviderApiKeyHelpCommand.CanExecute(null));

        page.SelectedAiProvider = page.AiProviders.Single(provider => provider.Id == "jan");

        Assert.Null(page.ApiKeyHelpUrl);
        Assert.False(page.ShowApiKeyHelp);
        Assert.False(page.OpenAiProviderApiKeyHelpCommand.CanExecute(null));
    }

    [Fact]
    public void Accept_PersistsEnabledSettingsAndKeepsHostSettings()
    {
        var plugin = CreatePlugin();
        var current = new Dictionary<string, string?> { ["unrelated"] = "value" };
        using var page = new AiProviderSettingsPageViewModel(plugin, current);

        page.SelectedAiProvider = page.AiProviders.Single(provider => provider.Id == "ollama");
        page.AiProviderModel = "custom-model";

        var result = page.Accept();

        Assert.True(result.Success);
        Assert.Same(current, result.Settings);
        var saved = plugin.GetSettings();
        Assert.True(saved.Enabled);
        Assert.Equal("ollama", saved.ProviderId);
        Assert.Equal("custom-model", saved.Model);
        Assert.Equal(11434, saved.Port);
    }

    [Fact]
    public void Accept_InvalidAddress_ReportsTheErrorWithoutSaving()
    {
        var plugin = CreatePlugin();
        using var page = new AiProviderSettingsPageViewModel(plugin, new Dictionary<string, string?>());
        page.SelectedAiProvider = page.AiProviders.Single(provider => provider.Id == "ollama");
        page.AiProviderAddress = "http://127.0.0.1";

        var result = page.Accept();

        Assert.False(result.Success);
        Assert.Equal(result.Error, page.AlertMessage);
        Assert.True(page.ShowAlert);
        Assert.False(plugin.GetSettings().Enabled);
        Assert.False(File.Exists(configurationPath));
    }
}
