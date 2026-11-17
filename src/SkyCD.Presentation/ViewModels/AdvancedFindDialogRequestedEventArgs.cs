using System;

namespace SkyCD.Presentation.ViewModels;

public sealed class AdvancedFindDialogRequestedEventArgs : EventArgs
{
    public required AdvancedCatalogFindDialogViewModel Dialog { get; init; }
}
