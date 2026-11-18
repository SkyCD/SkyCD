using System;

namespace SkyCD.Presentation.ViewModels;

public sealed class AiDescriptionBatchDialogRequestedEventArgs : EventArgs
{
    public required AiDescriptionBatchDialogViewModel Dialog { get; init; }
}
