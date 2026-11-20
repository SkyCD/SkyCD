using CommunityToolkit.Mvvm.ComponentModel;
using SkyCD.Documents;

namespace SkyCD.Presentation.ViewModels;

public partial class AiScanExclusionItemViewModel : ObservableObject
{
    [ObservableProperty] private string mimeType = string.Empty;

    public AiScanExclusionDocument ToDocument()
    {
        return new AiScanExclusionDocument { MimeType = MimeType.Trim() };
    }

    public static AiScanExclusionItemViewModel FromDocument(AiScanExclusionDocument document)
    {
        return new AiScanExclusionItemViewModel { MimeType = document.MimeType };
    }
}
