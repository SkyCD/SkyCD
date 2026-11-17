using System;

namespace SkyCD.Presentation.ViewModels;

public sealed class CollectionsDialogRequestedEventArgs : EventArgs
{
    public required CollectionsDialogViewModel Dialog { get; init; }
}
