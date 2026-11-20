using System.Windows.Input;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// One command the toolbar can host: a stable id, the localized category and label to show in the
/// customization dialog, an optional icon asset path, and the live command instance the toolbar invokes.
/// </summary>
public sealed class ToolbarCommandDescriptor
{
    public required string Id { get; init; }

    public required string Category { get; init; }

    public required string Label { get; init; }

    /// <summary>Application asset path such as <c>/Assets/new.png</c>; null for a text-only button.</summary>
    public string? IconPath { get; init; }

    public required ICommand Command { get; init; }

    public object? CommandParameter { get; init; }
}
