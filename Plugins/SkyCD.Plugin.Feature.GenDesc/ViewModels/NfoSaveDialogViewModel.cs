using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using SkyCD.Plugin.Feature.GenDesc.Localization;
using SkyCD.Plugin.Feature.GenDesc.Views;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Feature.GenDesc.ViewModels;

/// <summary>
/// Saves a generated .nfo through a picker the owning window supplies. Cancelling the picker writes nothing
/// and leaves the dialog open.
/// </summary>
public sealed partial class NfoSaveDialogViewModel : ObservableObject
{
    private readonly string content;
    private readonly Func<string, Task>? saved;

    public NfoSaveDialogViewModel(string title, string suggestedFileName, string content,
        Func<string, Task>? saved = null)
    {
        Title = title;
        SuggestedFileName = suggestedFileName;
        this.content = content;
        this.saved = saved;
    }

    public string Title { get; }

    public string SuggestedFileName { get; }

    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    private string errorText = string.Empty;

    public async Task SaveAsync(IGenDescFilePicker? picker)
    {
        if (picker is null)
        {
            ErrorText = LocalizationService.Current.Get(GenDescKeys.NoPicker);
            return;
        }

        var path = await picker.PickSaveAsync(LocalizationService.Current.Get(GenDescKeys.SaveNfoTitle),
            SuggestedFileName, new GenDescFileType("NFO", "*.nfo"));
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(path, content, new UTF8Encoding(false));
            StatusText = LocalizationService.Current.Format(GenDescKeys.NfoSaved, path);
            ErrorText = string.Empty;
            if (saved is not null)
            {
                await saved(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErrorText = LocalizationService.Current.Format(GenDescKeys.GenerationFailed, exception.Message);
        }
    }
}
