using CommunityToolkit.Mvvm.ComponentModel;

namespace SkyCD.Presentation.ViewModels;

/// <summary>A single discoverable MCP tool with its exposure toggle.</summary>
public sealed partial class McpToolItemViewModel : ObservableObject
{
    public McpToolItemViewModel(string name, string commandPath)
    {
        Name = name;
        CommandPath = commandPath;
    }

    public string Name { get; }

    /// <summary>The CLI command path, shown as the tool description.</summary>
    public string CommandPath { get; }

    [ObservableProperty] private bool isExposed;
}
