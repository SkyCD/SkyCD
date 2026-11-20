using System;

namespace SkyCD.Presentation.ViewModels;

public sealed class OptionsDialogRequestedEventArgs : EventArgs
{
    public required OptionsDialogViewModel Dialog { get; init; }

    public required Action<bool, string, string> Complete { get; init; }

    /// <summary>Localized settings category to select when the dialog opens.</summary>
    public string? PreselectSettingCategory { get; init; }
}