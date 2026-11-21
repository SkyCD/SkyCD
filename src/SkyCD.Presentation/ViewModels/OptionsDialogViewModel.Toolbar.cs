using System;
using System.Collections.Generic;
using SkyCD.Documents;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public partial class OptionsDialogViewModel
{
    private const int ToolbarCategoryIndex = 8;

    public ToolbarCustomizationViewModel? Toolbar { get; private set; }

    public bool IsToolbarCategorySelected => SelectedTabIndex == ToolbarCategoryIndex;

    public bool ShowToolbarSection =>
        IsToolbarCategorySelected &&
        IsCurrentCategoryVisibleInSearch;

    /// <summary>Raised when Save accepts an edited toolbar layout; the shell applies and persists it.</summary>
    public event EventHandler<ToolbarLayoutAppliedEventArgs>? ToolbarLayoutApplied;

    private string ToolbarCategoryName => LocalizationService.Current.Get(UiKeys.Toolbar);

    public void SetToolbarCustomization(
        IReadOnlyList<ToolbarCommandDescriptor>? commands,
        IEnumerable<ToolbarItemDocument>? layout)
    {
        Toolbar = new ToolbarCustomizationViewModel(commands ?? [], layout ?? ToolbarLayout.CreateDefault());
        OnPropertyChanged(nameof(Toolbar));
        OnPropertyChanged(nameof(ShowToolbarSection));
    }

    public IReadOnlyList<ToolbarItemDocument>? GetToolbarLayout() => Toolbar?.GetLayout();
}
