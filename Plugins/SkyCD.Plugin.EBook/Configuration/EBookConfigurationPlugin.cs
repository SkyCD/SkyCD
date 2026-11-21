using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Configuration;
using SkyCD.Plugin.EBook.Parsing;

namespace SkyCD.Plugin.EBook.Configuration;

/// <summary>Contributes an isolated eBook Options draft. Only the host persists accepted values.</summary>
public sealed class EBookConfigurationPlugin : IPluginConfigurationCapability
{
    public PluginConfigurationDescriptor Configuration { get; } = new("ebook.settings", "eBook Metadata");

    public Task<PluginConfigurationPage> CreatePageAsync(PluginConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var editor = new EBookConfigurationEditor(request.CurrentSettings);
        EBookConfigurationPage? view = null;
        return Task.FromResult(new PluginConfigurationPage(
            () => view = new EBookConfigurationPage(editor),
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
