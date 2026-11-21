using System;
using System.Collections.Generic;
using SkyCD.Documents;

namespace SkyCD.Presentation.ViewModels;

public sealed class ToolbarLayoutAppliedEventArgs : EventArgs
{
    public required IReadOnlyList<ToolbarItemDocument> Layout { get; init; }
}
