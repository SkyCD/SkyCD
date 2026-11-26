using System;
using System.Collections.Generic;
using SkyCD.Documents;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public partial class OptionsDialogViewModel
{
    private const string ToolbarCategoryId = "toolbar";
    private const int ToolbarCategoryIndex = 9;

    public ToolbarCustomizationViewModel? Toolbar { get; private set; }

    public bool IsToolbarCategorySelected => SelectedBuiltInIndex == ToolbarCategoryIndex;

    public bool ShowToolbarSection =>
        IsToolbarCategorySelected &&
        IsCurrentCategoryVisibleInSearch;

    /// <summary>Raised when Save accepts an edited toolbar layout; the shell applies and persists it.</summary>
    public event EventHandler<ToolbarLayoutAppliedEventArgs>? ToolbarLayoutApplied;

    public void SetToolbarCustomization(
        IReadOnlyList<ToolbarCommandDescriptor>? commands,
        IEnumerable<ToolbarItemDocument>? layout,
        IReadOnlyList<ToolbarItemDocument>? defaultLayout = null)
    {
        Toolbar = new ToolbarCustomizationViewModel(commands ?? [], layout ?? ToolbarLayout.CreateDefault(), defaultLayout);
        OnPropertyChanged(nameof(Toolbar));
        OnPropertyChanged(nameof(ShowToolbarSection));
    }

    public IReadOnlyList<ToolbarItemDocument>? GetToolbarLayout() => Toolbar?.GetLayout();
}
