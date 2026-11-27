using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using SkyCD.Plugin.Feature.GenDesc.Localization;
using SkyCD.Plugin.Feature.GenDesc.Models;
using SkyCD.Plugin.Feature.GenDesc.ViewModels;
using SkyCD.Plugin.Feature.GenDesc.Views;
using SkyCD.SDK.Capabilities.Configuration;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Feature.GenDesc.Configuration;

/// <summary>
/// Contributes the two GenDesc template pages to Options: info files and torrent-site post descriptions.
/// Templates are edited and stored by the page itself, so accepting a draft leaves plugin settings unchanged.
/// </summary>
public sealed class GenDescConfigurationPlugin : IPluginConfigurationCapability
{
    internal const string InfoFilesConfigurationId = "gendesc-info-files";
    internal const string PostDescriptionsConfigurationId = "gendesc-post-descriptions";

    /// <inheritdoc />
    public IReadOnlyList<PluginConfigurationDescriptor> Configurations =>
    [
        new(InfoFilesConfigurationId, LocalizationService.Current.Get(GenDescKeys.InfoFilesTitle),
            LocalizationService.Current.Get(GenDescKeys.OptionsGroup), 50),
        new(PostDescriptionsConfigurationId, LocalizationService.Current.Get(GenDescKeys.PostDescriptionsTitle),
            LocalizationService.Current.Get(GenDescKeys.OptionsGroup), 51)
    ];

    /// <summary>Overrides the user template directory in tests; production uses the shared default.</summary>
    internal string? TemplateDirectory { get; set; }

    /// <inheritdoc />
    public Task<PluginConfigurationPage> CreatePageAsync(PluginConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var viewModel = CreateTemplatesViewModel(request.ConfigurationId);
        return Task.FromResult(new PluginConfigurationPage(
            () => viewModel is null
                ? new TextBlock
                {
                    Text = $"Unsupported configuration '{request.ConfigurationId}'.",
                    TextWrapping = TextWrapping.Wrap
                }
                : new GenDescTemplatesView { DataContext = viewModel },
            token =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new PluginConfigurationResult
                {
                    Success = viewModel is not null,
                    Error = viewModel is null ? $"Unsupported configuration '{request.ConfigurationId}'." : null,
                    Settings = request.CurrentSettings
                });
            }));
    }

    /// <summary>Creates the template editor for the given configuration id, or null for an unknown id.</summary>
    internal GenDescTemplatesViewModel? CreateTemplatesViewModel(string configurationId) =>
        TemplateKind(configurationId) is { } kind
            ? new GenDescTemplatesViewModel(kind, TemplateDirectory)
            : null;

    private static string? TemplateKind(string configurationId) =>
        configurationId.Equals(InfoFilesConfigurationId, StringComparison.OrdinalIgnoreCase) ? TemplateInfo.NfoKind
        : configurationId.Equals(PostDescriptionsConfigurationId, StringComparison.OrdinalIgnoreCase)
            ? TemplateInfo.DescriptionKind
            : null;
}
