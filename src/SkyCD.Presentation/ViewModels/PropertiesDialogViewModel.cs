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
        Name = name;
        IconGlyph = iconGlyph;
        this.comments = comments;
        InfoProperties = infoProperties;
    }

    public string ObjectKey { get; }

    public string Name { get; }

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
