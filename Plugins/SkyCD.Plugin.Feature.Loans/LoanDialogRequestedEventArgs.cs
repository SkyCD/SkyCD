using System;

namespace SkyCD.Plugin.Feature.Loans;

public sealed class LoanDialogRequestedEventArgs(LoanDialogViewModel dialog) : EventArgs
{
    public LoanDialogViewModel Dialog { get; } = dialog;
}
