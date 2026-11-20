using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using SkyCD.Plugin.Abstractions.Capabilities.Configuration;

namespace SkyCD.Plugin.AudioCd;

/// <summary>
/// Contributes the audio CD settings surface to Options → Plugins → Configure. In a headless host the current
/// (normalized) settings are returned unchanged so configuration never fails without a window.
/// </summary>
public sealed class AudioCdConfigurationPlugin : IPluginConfigurationCapability
{
    public PluginConfigurationDescriptor Configuration { get; } = new(
        "skycd.audio-cd.settings",
        "Audio CD Metadata",
        Width: 520,
        Height: 470);

    public async Task<PluginConfigurationResult> ConfigureAsync(PluginConfigurationRequest request,
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

        var editor = new AudioCdConfigurationEditor(request.CurrentSettings);
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

        var window = new AudioCdConfigurationWindow(editor)
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
