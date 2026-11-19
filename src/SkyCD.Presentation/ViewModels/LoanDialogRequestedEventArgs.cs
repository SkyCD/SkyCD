using System;

namespace SkyCD.Presentation.ViewModels;

public sealed class LoanDialogRequestedEventArgs(LoanDialogViewModel dialog) : EventArgs
{
    public LoanDialogViewModel Dialog { get; } = dialog;
}
