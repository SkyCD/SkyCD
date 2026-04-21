using CommunityToolkit.Mvvm.ComponentModel;

namespace SkyCD.Presentation.ViewModels;

public partial class AddingProgressDialogViewModel : ObservableObject
{
    private readonly IReadOnlyDictionary<string, string> _translations;

    public IReadOnlyDictionary<string, string> Translations => _translations;

    public AddingProgressDialogViewModel(IReadOnlyDictionary<string, string> translations)
    {
        _translations = translations ?? throw new ArgumentNullException(nameof(translations));
        OperationText = _translations.TryGetValue("AddingProgressWindow.OperationText", out var text) ? text : "Preparing database for modifications...";
    }

    public AddingProgressDialogViewModel() : this(new Dictionary<string, string>())
    {
    }

    [ObservableProperty]
    private string operationText;

    [ObservableProperty]
    private int progressValue;
}
