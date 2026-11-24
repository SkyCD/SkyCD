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

    /// <summary>
    /// Icon reference: a key of the toolbar artwork (<c>ToolbarIcon.Undo</c>, colored Twemoji first and the
    /// glyph dictionary <c>avares://SkyCD.App/Styles/ToolbarIcons.axaml</c> as fallback) or a plain
    /// application asset path such as <c>/Assets/new.png</c>. Every toolbar command carries one; null only
    /// means a text-only button, which no shipped command uses.
    /// </summary>
    public string? IconPath { get; init; }

    public required ICommand Command { get; init; }

    public object? CommandParameter { get; init; }

    /// <summary>
    /// Whether the command is a checkable view toggle. Non-toggle commands never report a checked state
    /// and keep invoke-only automation semantics.
    /// </summary>
    public bool IsToggle { get; init; }

    /// <summary>
    /// The current checked state of a toggle command, derived from the same view-model state the View menu
    /// uses, so the toolbar and the menu never disagree.
    /// </summary>
    public bool IsChecked { get; init; }
}
