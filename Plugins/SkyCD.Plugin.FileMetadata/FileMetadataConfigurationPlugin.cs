using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using SkyCD.Plugin.Abstractions.Capabilities.Configuration;

namespace SkyCD.Plugin.FileMetadata;

/// <summary>
/// Contributes the options surface shown in the reference: keyword/tag collection, hierarchical tag
/// splitting, comments and rating. Accepted values are persisted by the host and pushed back into the
/// capability, so no restart is needed after a change.
/// </summary>
public sealed class FileMetadataConfigurationPlugin : IPluginConfigurationCapability
{
    public PluginConfigurationDescriptor Configuration { get; } = new(
        "file-metadata.settings",
        "File Metadata",
        Width: 480,
        Height: 380);

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
        var editor = new FileMetadataConfigurationEditor(request.CurrentSettings);
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

        var window = new FileMetadataConfigurationWindow(editor)
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
