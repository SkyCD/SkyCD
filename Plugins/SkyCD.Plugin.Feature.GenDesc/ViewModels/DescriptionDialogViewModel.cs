using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Plugin.Feature.GenDesc.Localization;
using SkyCD.SDK.Capabilities.Ui;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Feature.GenDesc.ViewModels;

/// <summary>Shows generated site text and copies it to the clipboard.</summary>
public sealed partial class DescriptionDialogViewModel : ObservableObject
{
    private readonly IClipboardApi? clipboard;

    public DescriptionDialogViewModel(string title, string text, IClipboardApi? clipboard)
    {
        Title = title;
        this.text = text;
        this.clipboard = clipboard;
    }

    public string Title { get; }

    [ObservableProperty]
    private string text;

    [ObservableProperty]
    private string statusText = string.Empty;

    [RelayCommand]
    private async Task CopyAsync()
    {
        if (clipboard is null)
        {
            return;
        }

        await clipboard.SetTextAsync(Text);
        StatusText = LocalizationService.Current.Get(GenDescKeys.Copied);
    }
}
