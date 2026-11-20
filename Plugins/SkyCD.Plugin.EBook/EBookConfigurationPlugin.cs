using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using SkyCD.Plugin.Abstractions.Capabilities.Configuration;

namespace SkyCD.Plugin.EBook;

/// <summary>
/// Contributes the eBook configuration surface. Accepted values are persisted by the host and pushed back
/// into both extraction capabilities, so no restart is needed after a change.
/// </summary>
public sealed class EBookConfigurationPlugin : IPluginConfigurationCapability
{
    public PluginConfigurationDescriptor Configuration { get; } = new(
        "ebook.settings",
        "eBook Metadata",
        Width: 460,
        Height: 430);

    public async Task<PluginConfigurationResult> ConfigureAsync(
        PluginConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConfigurationId.Equals(Configuration.ConfigurationId, StringComparison.OrdinalIgnoreCase))
        {
            return new PluginConfigurationResult
            {
                Success = false,
                Error = $"Unsupported configuration '{request.ConfigurationId}'."
            };
        }

        cancellationToken.ThrowIfCancellationRequested();
        var editor = new EBookConfigurationEditor(request.CurrentSettings);
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

        var window = new EBookConfigurationWindow(editor)
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
