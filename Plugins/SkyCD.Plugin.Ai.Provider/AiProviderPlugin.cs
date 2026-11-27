using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using SkyCD.Plugin.Ai.Provider.Configuration;
using SkyCD.Plugin.Ai.Provider.Exceptions;
using SkyCD.Plugin.Ai.Provider.Models;
using SkyCD.Plugin.Ai.Provider.Sources;
using SkyCD.Plugin.Ai.Provider.Views;
using SkyCD.SDK.Capabilities.Configuration;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Ai.Provider;

/// <summary>
/// Generates descriptions by calling an OpenAI-compatible provider selected by the user. It shares the
/// provider-neutral description contract with the ACP plugin, so host-side scan exclusions and file-size
/// limits apply to either implementation.
/// </summary>
public sealed class AiProviderPlugin : IGeneratedDescriptionCapability, IDescriptionGenerationPlanner,
    IAiDescriptionImplementationCapability, IPluginConfigurationCapability
{
    public const string PluginVersion = "1.0.0";

    /// <summary>Configuration page that edits the provider connection.</summary>
    public const string SettingsConfigurationId = "ai-provider";

    private static readonly DescriptionContextOptions ContextOptions =
        new(Filename: true, Metadata: false, Text: true, Images: true, Audio: false,
            MaxTextCharacters: 12000, MaxArtifactBytes: 4 * 1024 * 1024);

    private readonly AiProviderSettingsStore store;
    private readonly AiProviderClient client;

    public AiProviderPlugin() : this(DefaultConfigurationPath(), null)
    {
    }

    public AiProviderPlugin(string configurationPath, HttpClient? httpClient = null)
    {
        store = new AiProviderSettingsStore(configurationPath);
        client = new AiProviderClient(httpClient);
    }

    public string ImplementationId => AiDescriptionImplementationIds.Provider;

    public string ImplementationName => "AI provider";

    public IReadOnlyList<AiProviderChoice> Providers => AiProviderCatalog.Providers;

    /// <inheritdoc />
    public IReadOnlyList<PluginConfigurationDescriptor> Configurations =>
        [new(SettingsConfigurationId, LocalizationService.Current.Get(UiKeys.AiProviderCategory), Order: -10)];

    /// <inheritdoc />
    public Task<PluginConfigurationPage> CreatePageAsync(PluginConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!request.ConfigurationId.Equals(SettingsConfigurationId, StringComparison.OrdinalIgnoreCase))
        {
            var error = $"Unsupported configuration '{request.ConfigurationId}'.";
            return Task.FromResult(new PluginConfigurationPage(
                () => new TextBlock
                {
                    Text = error,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                _ => Task.FromResult(new PluginConfigurationResult { Success = false, Error = error })));
        }

        var viewModel = new AiProviderSettingsPageViewModel(this, request.CurrentSettings);
        return Task.FromResult(new PluginConfigurationPage(
            () => new AiProviderSettingsView(viewModel),
            token =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(viewModel.Accept());
            },
            () => viewModel.Dispose()));
    }

    public MediaMetadataSchema Schema => new("skycd.ai-description.metadata", 1,
        new[]
        {
            "AiDescription", "AiDescriptionAgent", "AiDescriptionModel", "AiDescriptionPromptVersion",
            "AiDescriptionLanguage", "AiDescriptionOrigin", "AiDescriptionProcessingDestination",
            "AiDescriptionContextFingerprint", "AiDescriptionSourceFingerprint"
        }.Select(key => new MediaMetadataField(key, MediaMetadataType.Text)).ToArray());

    public DescriptionGenerationPlan GetPlan()
    {
        try
        {
            var settings = store.Read();
            var provider = AiProviderCatalog.Find(settings.ProviderId) ?? AiProviderCatalog.Default;
            if (!settings.Enabled)
            {
                return Unavailable(provider.Name, settings.Model,
                    "generation is disabled; configure and save the AI provider in Options");
            }

            AiProviderSettingsValidator.Validate(settings, requireEnabled: true);
            return new DescriptionGenerationPlan(true, provider.Name, PluginVersion, settings.Model,
                AiProviderCatalog.IsLoopback(settings) ? "local-inference" : "cloud", ContextOptions);
        }
        catch (AiProviderException exception)
        {
            return Unavailable("AI provider", null, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Unavailable("AI provider", null, $"configuration cannot be read: {exception.GetType().Name}");
        }
    }

    public Task<GeneratedDescription> GenerateAsync(DescriptionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        AiProviderSettings settings;
        try
        {
            settings = store.Read();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new AiProviderException($"configuration cannot be read: {exception.GetType().Name}");
        }

        return client.GenerateAsync(settings, context, cancellationToken);
    }

    public AiProviderSettings GetSettings() => store.Read();

    public void SaveSettings(AiProviderSettings settings)
    {
        AiProviderSettingsValidator.Validate(settings, requireEnabled: false);
        store.Write(settings);
    }

    public string BuildEndpointUrl(AiProviderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return AiProviderCatalog.BuildEndpoint(settings);
    }

    public Task<AiProviderConnectionResult> CheckConnectionAsync(AiProviderSettings settings,
        CancellationToken cancellationToken) => client.CheckConnectionAsync(settings, cancellationToken);

    public Task<IReadOnlyList<string>> ListModelsAsync(AiProviderSettings settings,
        CancellationToken cancellationToken) => client.ListModelsAsync(settings, cancellationToken);

    public Task<string?> StartServerAsync(AiProviderSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(settings);
        return Task.FromResult(AiProviderServerLauncher.Start(settings));
    }

    private static DescriptionGenerationPlan Unavailable(string providerName, string? model, string reason) =>
        new(false, providerName, PluginVersion, model, "unknown", ContextOptions, reason);

    private static string DefaultConfigurationPath() => Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
        "SkyCD", "ai-provider.json");
}
