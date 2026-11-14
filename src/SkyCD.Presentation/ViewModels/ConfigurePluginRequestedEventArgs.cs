using System;

namespace SkyCD.Presentation.ViewModels;

public sealed class ConfigurePluginRequestedEventArgs : EventArgs
{
    public required string PluginId { get; init; }

    public required string PluginName { get; init; }
}
