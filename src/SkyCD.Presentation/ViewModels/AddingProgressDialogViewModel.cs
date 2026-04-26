using CommunityToolkit.Mvvm.ComponentModel;
using SkyCD.Plugin.Abstractions.Localization;

namespace SkyCD.Presentation.ViewModels;

public partial class AddingProgressDialogViewModel : ObservableObject
{
    public AddingProgressDialogViewModel()
        : this(new I18nService())
    {
    }

    public AddingProgressDialogViewModel(II18nService i18n)
    {
        operationText = i18n.Get("progress.preparing_database");
    }

    [ObservableProperty]
    private string operationText = string.Empty;

    [ObservableProperty]
    private int progressValue;
}
