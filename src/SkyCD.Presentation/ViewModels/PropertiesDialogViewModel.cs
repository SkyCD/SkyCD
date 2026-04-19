using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SkyCD.Presentation.ViewModels;

public partial class PropertiesDialogViewModel : ObservableObject
{
    public PropertiesDialogViewModel(
        string objectKey,
        string name,
        string iconGlyph,
        string comments,
        IReadOnlyList<PropertiesInfoItem> infoProperties)
    {
        ObjectKey = objectKey;
        this.name = name;
        IconGlyph = iconGlyph;
        this.comments = comments;
        InfoProperties = infoProperties;
    }

    public string ObjectKey { get; }

    [ObservableProperty]
    private string name;

    public string IconGlyph { get; }

    public IReadOnlyList<PropertiesInfoItem> InfoProperties { get; }

    public bool HasInfoTab => InfoProperties.Count > 0;

    [ObservableProperty]
    private string comments;

    [ObservableProperty]
    private bool dialogAccepted;

    [RelayCommand]
    private void Confirm()
    {
        DialogAccepted = true;
    }
}
