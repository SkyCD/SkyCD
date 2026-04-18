using CommunityToolkit.Mvvm.ComponentModel;

namespace SkyCD.Presentation.ViewModels;

public partial class AddingProgressDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string operationText = "Preparing database for modifications...";

    [ObservableProperty]
    private int progressValue;
}
