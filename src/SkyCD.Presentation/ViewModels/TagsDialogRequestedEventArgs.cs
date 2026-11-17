using System;

namespace SkyCD.Presentation.ViewModels;

public sealed class TagsDialogRequestedEventArgs : EventArgs
{
    public required TagsDialogViewModel Dialog { get; init; }
}
