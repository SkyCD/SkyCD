using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Configuration;

namespace SkyCD.Plugin.Metadata.EComic.Configuration;

/// <summary>Contributes an isolated eComic Options draft. Only the host persists accepted values.</summary>
public sealed class EComicConfigurationPlugin : IPluginConfigurationCapability
{
    public IReadOnlyList<PluginConfigurationDescriptor> Configurations { get; } =
        [new("ecomic.settings", "eComic", "Metadata")];

    public Task<PluginConfigurationPage> CreatePageAsync(PluginConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var supported = Configurations.Any(descriptor =>
            descriptor.ConfigurationId.Equals(request.ConfigurationId, StringComparison.OrdinalIgnoreCase));
        var editor = new EComicConfigurationEditor(request.CurrentSettings);
        EComicConfigurationPage? view = null;
        return Task.FromResult(new PluginConfigurationPage(
            () => view = new EComicConfigurationPage(editor),
            token =>
            {
                token.ThrowIfCancellationRequested();
                view?.UpdateDraft();
                return Task.FromResult(new PluginConfigurationResult
                {
                    Success = supported,
                    Error = supported ? null : $"Unsupported configuration '{request.ConfigurationId}'.",
                    Settings = editor.BuildSettings()
                });
            }));
    }
}
