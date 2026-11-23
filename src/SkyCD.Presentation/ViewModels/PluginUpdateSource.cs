namespace SkyCD.Presentation.ViewModels;

/// <summary>Where a discovered plugin receives updates from; <c>null</c> means the install state is unknown.</summary>
public enum PluginUpdateSource
{
    PluginFolder,
    Marketplace
}
