using System;

namespace SkyCD.Presentation.ViewModels;

public sealed class LocationsDialogRequestedEventArgs : EventArgs
{
    public required LocationsDialogViewModel Dialog { get; init; }

    public required Action<bool> Complete { get; init; }
}
