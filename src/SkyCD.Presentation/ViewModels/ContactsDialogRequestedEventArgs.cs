using System;

namespace SkyCD.Presentation.ViewModels;

public sealed class ContactsDialogRequestedEventArgs : EventArgs
{
    public required ContactsDialogViewModel Dialog { get; init; }
}
