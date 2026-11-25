using System;

namespace SkyCD.Presentation.ViewModels;

public sealed class OptionsDialogRequestedEventArgs : EventArgs
{
    public required OptionsDialogViewModel Dialog { get; init; }

    public required Action<bool, string, string> Complete { get; init; }

    /// <summary>Stable settings category id to select when the dialog opens.</summary>
    public string? PreselectCategoryId { get; init; }
}