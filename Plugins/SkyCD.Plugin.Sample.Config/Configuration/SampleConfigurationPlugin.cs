using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Configuration;

namespace SkyCD.Plugin.Sample.Config.Configuration;

/// <summary>Demonstrates an embedded, session-local Options draft with no UI dependency for headless use.</summary>
public sealed class SampleConfigurationPlugin : IPluginConfigurationCapability
{
    public IReadOnlyList<PluginConfigurationDescriptor> Configurations { get; } = [new(
        "sample.configuration.general", "Sample Configuration")];

    public Task<PluginConfigurationPage> CreatePageAsync(PluginConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var supported = Configurations.Any(descriptor =>
            descriptor.ConfigurationId.Equals(request.ConfigurationId, StringComparison.OrdinalIgnoreCase));
        var editor = new SampleConfigurationEditor(request.CurrentSettings);
        SampleConfigurationPage? view = null;
        return Task.FromResult(new PluginConfigurationPage(
            () => view = new SampleConfigurationPage(editor),
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
