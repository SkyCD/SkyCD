using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Configuration;

namespace SkyCD.Plugin.Metadata.Fonts.Configuration;

/// <summary>Contributes an isolated font Options draft. Only the host persists accepted values.</summary>
public sealed class FontConfigurationPlugin : IPluginConfigurationCapability
{
    public PluginConfigurationDescriptor Configuration { get; } = new("fonts.settings", "Fonts and Previews", "Metadata");

    public Task<PluginConfigurationPage> CreatePageAsync(PluginConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var editor = new FontConfigurationEditor(request.CurrentSettings);
        FontConfigurationPage? view = null;
        return Task.FromResult(new PluginConfigurationPage(
            () => view = new FontConfigurationPage(editor),
            token =>
            {
                token.ThrowIfCancellationRequested();
                view?.UpdateDraft();
                return Task.FromResult(new PluginConfigurationResult
                {
                    Success = request.ConfigurationId.Equals(Configuration.ConfigurationId, StringComparison.OrdinalIgnoreCase),
                    Error = request.ConfigurationId.Equals(Configuration.ConfigurationId, StringComparison.OrdinalIgnoreCase)
                        ? null : $"Unsupported configuration '{request.ConfigurationId}'.",
                    Settings = editor.BuildSettings()
                });
            }));
    }
}
