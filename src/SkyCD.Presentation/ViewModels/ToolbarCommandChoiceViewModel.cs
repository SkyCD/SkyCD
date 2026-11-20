using CommunityToolkit.Mvvm.ComponentModel;

namespace SkyCD.Presentation.ViewModels;

/// <summary>A command offered in the toolbar customization dialog.</summary>
public sealed partial class ToolbarCommandChoiceViewModel : ObservableObject
{
    public ToolbarCommandChoiceViewModel(ToolbarCommandDescriptor descriptor)
    {
        Descriptor = descriptor;
    }

    public ToolbarCommandDescriptor Descriptor { get; }

    public string Category => Descriptor.Category;

    public string Label => Descriptor.Label;

    public string? IconPath => Descriptor.IconPath;

    /// <summary>A command appears at most once, so an added command is listed but cannot be added again.</summary>
    [ObservableProperty] private bool isInToolbar;
}
