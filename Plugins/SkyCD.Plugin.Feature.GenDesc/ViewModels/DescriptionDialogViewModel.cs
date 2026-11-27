using CommunityToolkit.Mvvm.ComponentModel;
using SkyCD.Plugin.Feature.GenDesc.Localization;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Feature.GenDesc.ViewModels;

/// <summary>Shows generated site text; the owning window copies it to the clipboard.</summary>
public sealed partial class DescriptionDialogViewModel : ObservableObject
{
    public DescriptionDialogViewModel(string title, string text)
    {
        Title = title;
        this.text = text;
    }

    public string Title { get; }

    [ObservableProperty]
    private string text;

    [ObservableProperty]
    private string statusText = string.Empty;

    /// <summary>Reports a successful copy after the window has placed the text on the clipboard.</summary>
    public void MarkCopied() => StatusText = LocalizationService.Current.Get(GenDescKeys.Copied);
}
