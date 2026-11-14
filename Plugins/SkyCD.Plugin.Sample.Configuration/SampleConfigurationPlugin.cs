using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using SkyCD.Plugin.Abstractions.Capabilities.Configuration;

namespace SkyCD.Plugin.Sample.Configuration;

/// <summary>
/// Sample plugin that contributes a configuration surface to the host.
/// </summary>
public sealed class SampleConfigurationPlugin : IPluginConfigurationCapability
{
    public PluginConfigurationDescriptor Configuration { get; } = new(
        "sample.configuration.general",
        "Sample Configuration",
        Width: 420,
        Height: 240);

    public async Task<PluginConfigurationResult> ConfigureAsync(
        PluginConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.ConfigurationId.Equals(Configuration.ConfigurationId, StringComparison.OrdinalIgnoreCase))
        {
            return new PluginConfigurationResult
            {
                Success = false,
                Error = $"Unsupported configuration '{request.ConfigurationId}'."
            };
        }

        var editor = new SampleConfigurationEditor(request.CurrentSettings);
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (lifetime?.MainWindow is null)
        {
            // Headless hosts (CLI, tests) cannot show a window and keep the current values.
            return new PluginConfigurationResult
            {
                Success = true,
                Settings = editor.BuildSettings()
            };
        }

        var window = new SampleConfigurationWindow(editor)
        {
            Title = Configuration.Title,
            Width = Configuration.Width,
            Height = Configuration.Height
        };

        var accepted = await window.ShowDialog<bool?>(lifetime.MainWindow);
        if (accepted != true)
        {
            return new PluginConfigurationResult
            {
                Success = true,
                Canceled = true
            };
        }

        return new PluginConfigurationResult
        {
            Success = true,
            Settings = editor.BuildSettings()
        };
    }
}
