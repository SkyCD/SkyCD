namespace SkyCD.Presentation.ViewModels;

public sealed class PropertiesDialogRequestedEventArgs : EventArgs
{
    public required PropertiesDialogViewModel Dialog { get; init; }

    public required Action<bool, string> Complete { get; init; }
}