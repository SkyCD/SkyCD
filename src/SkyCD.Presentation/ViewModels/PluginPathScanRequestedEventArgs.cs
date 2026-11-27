using System;

namespace SkyCD.Presentation.ViewModels;

/// <summary>Folder the live plug-in runtime should be rescanned from, captured when the request fired.</summary>
public sealed class PluginPathScanRequestedEventArgs : EventArgs
{
    public required string Path { get; init; }
}
