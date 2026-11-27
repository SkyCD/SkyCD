using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Configuration;
using SkyCD.Plugin.Metadata.EBook.Parsing;

namespace SkyCD.Plugin.Metadata.EBook.Configuration;

/// <summary>Contributes an isolated eBook Options draft. Only the host persists accepted values.</summary>
public sealed class EBookConfigurationPlugin : IPluginConfigurationCapability
{
    public IReadOnlyList<PluginConfigurationDescriptor> Configurations { get; } =
        [new("ebook.settings", "eBook", "Metadata")];

    public Task<PluginConfigurationPage> CreatePageAsync(PluginConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var supported = Configurations.Any(descriptor =>
            descriptor.ConfigurationId.Equals(request.ConfigurationId, StringComparison.OrdinalIgnoreCase));
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
                    Success = supported,
                    Error = supported ? null : $"Unsupported configuration '{request.ConfigurationId}'.",
                    Settings = editor.BuildSettings()
                });
            }));
    }
}
