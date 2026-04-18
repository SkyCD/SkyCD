namespace SkyCD.Presentation.ViewModels;

public sealed class OptionsDialogRequestedEventArgs : EventArgs
{
    public required OptionsDialogViewModel Dialog { get; init; }

    public required Action<bool, string, string> Complete { get; init; }
}
