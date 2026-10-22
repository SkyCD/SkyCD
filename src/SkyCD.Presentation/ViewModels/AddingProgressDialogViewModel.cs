using SkyCD.UI.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SkyCD.Presentation.ViewModels;

public partial class AddingProgressDialogViewModel : ObservableObject
{
    [ObservableProperty] private string operationText = LocalizationService.Current.Get(TextKeys.PreparingDatabaseForModifications);

    [ObservableProperty] private int progressValue;
}