using System;
using System.Linq;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using SkyCD.App.Icons;
using SkyCD.Documents;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Controls.Toolbars;

namespace SkyCD.App.Views;

public partial class MainWindow
{
    private void OnToolbarItemsChanged(object? sender, EventArgs e)
    {
        if (sender is MainWindowViewModel viewModel)
        {
            RebuildToolbar(viewModel);
        }
    }

    private void OnToolbarStateChanged(object? sender, EventArgs e)
    {
        if (sender is not MainWindowViewModel viewModel)
        {
            return;
        }

        foreach (var command in viewModel.ToolbarCommands())
        {
            if (command.IsToggle)
            {
                MainToolbar.SetCheckedState(command.Id, command.IsChecked);
            }
        }
    }

    private void OnToolbarLayoutChanged(object? sender, EventArgs e)
    {
        if (sender is not MainWindowViewModel viewModel)
        {
            return;
        }

        var options = LoadAppOptions();
        options.Toolbar = viewModel.ToolbarEntries.ToList();
        SaveAppOptions(options);
    }

    private void RebuildToolbar(MainWindowViewModel viewModel)
    {
        var commands = viewModel.ToolbarCommands().ToDictionary(command => command.Id, StringComparer.OrdinalIgnoreCase);
        MainToolbar.Items.Clear();
        foreach (var entry in viewModel.ToolbarEntries)
        {
            if (string.Equals(entry.Kind, ToolbarItemDocument.SeparatorKind, StringComparison.OrdinalIgnoreCase))
            {
                MainToolbar.Items.Add(new ClassicToolbarSeparator());
                continue;
            }

            if (entry.CommandId is not { } commandId || !commands.TryGetValue(commandId, out var descriptor))
            {
                continue;
            }

            MainToolbar.Items.Add(new ClassicToolbarButton
            {
                CommandId = descriptor.Id,
                Command = descriptor.Command,
                CommandParameter = descriptor.CommandParameter,
                Text = descriptor.Label,
                ImageSrc = LoadToolbarIcon(descriptor.IconPath),
                IsToggle = descriptor.IsToggle,
                IsChecked = descriptor.IsChecked
            });
        }
    }

    private IImage? LoadToolbarIcon(string? iconPath)
    {
        if (string.IsNullOrEmpty(iconPath))
        {
            return null;
        }

        if (toolbarIcons.TryGetValue(iconPath, out var cached))
        {
            return cached;
        }

        // Keys of the project-owned glyph dictionary render as vector images; asset paths fall back to bitmaps.
        var icon = ToolbarIconResolver.Resolve(iconPath)
            ?? throw new InvalidOperationException($"Toolbar command icon '{iconPath}' could not be resolved.");
        toolbarIcons[iconPath] = icon;
        return icon;
    }
}
