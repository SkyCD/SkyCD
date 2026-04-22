using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SkyCD.Presentation.ViewModels;

public partial class AboutDialogViewModel : ObservableObject
{
    [ObservableProperty] private bool dialogAccepted;

    public AboutDialogViewModel()
        : this("SkyCD", "3.0.0", "https://github.com/SkyCD/SkyCD")
    {
    }

    public AboutDialogViewModel(string productName, string version, string website)
    {
        ProductName = productName;
        Version = version;
        Website = website;
    }

    public string ProductName { get; }

    public string Version { get; }

    public string Website { get; }

    [RelayCommand]
    private void Confirm()
    {
        DialogAccepted = true;
    }
}