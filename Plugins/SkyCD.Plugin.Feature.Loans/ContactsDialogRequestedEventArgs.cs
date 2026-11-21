using System;

namespace SkyCD.Plugin.Feature.Loans;

public sealed class ContactsDialogRequestedEventArgs : EventArgs
{
    public required ContactsDialogViewModel Dialog { get; init; }
}
