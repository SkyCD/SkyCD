using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Configuration;

namespace SkyCD.Plugin.Metadata.Audio.AudioCD.Configuration;

/// <summary>Contributes an isolated audio CD Options draft. Only the host persists accepted values.</summary>
public sealed class AudioCdConfigurationPlugin : IPluginConfigurationCapability
{
    public IReadOnlyList<PluginConfigurationDescriptor> Configurations { get; } = [new(
        "skycd.audio-cd.settings", "Audio CD", "Metadata")];

    public Task<PluginConfigurationPage> CreatePageAsync(PluginConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var supported = Configurations.Any(descriptor =>
            descriptor.ConfigurationId.Equals(request.ConfigurationId, StringComparison.OrdinalIgnoreCase));
        var editor = new AudioCdConfigurationEditor(request.CurrentSettings);
        AudioCdConfigurationPage? view = null;
        return Task.FromResult(new PluginConfigurationPage(
            () => view = new AudioCdConfigurationPage(editor),
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
