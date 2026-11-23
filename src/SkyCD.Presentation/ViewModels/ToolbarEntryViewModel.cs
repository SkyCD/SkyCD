using SkyCD.Documents;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

/// <summary>One row in the toolbar list being edited: a command button or a separator.</summary>
public sealed class ToolbarEntryViewModel
{
    private ToolbarEntryViewModel(ToolbarCommandDescriptor? descriptor, string separatorLabel)
    {
        Descriptor = descriptor;
        Label = descriptor is null ? separatorLabel : MnemonicText.Strip(descriptor.Label);
    }

    public ToolbarCommandDescriptor? Descriptor { get; }

    public string? CommandId => Descriptor?.Id;

    public bool IsSeparator => Descriptor is null;

    public string Label { get; }

    /// <summary>The command's icon reference; a separator has none and renders as a plain line.</summary>
    public string? IconPath => Descriptor?.IconPath;

    public static ToolbarEntryViewModel FromCommand(ToolbarCommandDescriptor descriptor)
    {
        System.ArgumentNullException.ThrowIfNull(descriptor);
        return new ToolbarEntryViewModel(descriptor, string.Empty);
    }

    public static ToolbarEntryViewModel CreateSeparator()
    {
        return new ToolbarEntryViewModel(null, LocalizationService.Current.Get(UiKeys.Separator));
    }

    public ToolbarItemDocument ToDocument()
    {
        return Descriptor is null
            ? ToolbarItemDocument.Separator()
            : ToolbarItemDocument.Command(Descriptor.Id);
    }
}
