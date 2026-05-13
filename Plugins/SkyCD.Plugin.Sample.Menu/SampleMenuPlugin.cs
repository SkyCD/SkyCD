using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using SkyCD.Plugin.Abstractions.Capabilities.Menu;

namespace SkyCD.Plugin.Sample.Menu;

public sealed class SampleMenuPlugin : IMenuPluginCapability
{
    public IReadOnlyCollection<MenuContribution> GetMenuContributions() =>
    [
        new("sample.menu.example", "Example", "Tools", Order: 100)
    ];

    public Task ExecuteMenuCommandAsync(string commandId, MenuCommandContext context,
        CancellationToken cancellationToken = default)
    {
        if (!commandId.Equals("sample.menu.example", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var dialog = new NotificationWindow(
            "did you thought that this sample plugin can do something useful? \U0001F609");
        if (lifetime?.MainWindow is not null)
        {
            dialog.ShowDialog(lifetime.MainWindow);
        }
        else
        {
            dialog.Show();
        }

        return Task.CompletedTask;
    }
}