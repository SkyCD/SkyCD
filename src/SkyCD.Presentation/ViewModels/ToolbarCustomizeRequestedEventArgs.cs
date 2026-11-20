using System;

namespace SkyCD.Presentation.ViewModels;

public sealed class ToolbarCustomizeRequestedEventArgs : EventArgs
{
    public required ToolbarCustomizationViewModel Dialog { get; init; }
}
